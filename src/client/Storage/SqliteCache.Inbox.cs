using System.Text.Json;
using Chat.Core.Messaging;
using Chat.Protocol;
using Microsoft.Data.Sqlite;

namespace Chat.Storage;

public sealed partial class SqliteCache
{
    public Task<IReadOnlyList<ChannelInbox>> LoadInboxAsync(CacheScope scope, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<ChannelInbox>>(() =>
        {
            using var connection = Open();
            var rows = new Dictionary<Guid, ChannelInbox>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    """
                    SELECT channel_id, last_read_id, last_message_id, last_author_id, last_mention_id, notify, draft
                    FROM channel_inbox WHERE instance_id=$instance AND account_id=$account
                    """;
                Scope(command, scope);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var id = Guid.Parse(reader.GetString(0));
                    rows[id] = new(
                        id,
                        ReadId(reader, 1),
                        ReadId(reader, 2),
                        ReadId(reader, 3),
                        ReadId(reader, 4),
                        ChannelInbox.ParseNotify(reader.IsDBNull(5) ? null : reader.GetString(5)),
                        reader.IsDBNull(6) ? null : reader.GetString(6));
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    """
                    SELECT m.channel_id, m.id, m.payload FROM messages m
                    JOIN (
                        SELECT channel_id, MAX(id) AS id FROM messages
                        WHERE instance_id=$instance AND account_id=$account
                        GROUP BY channel_id
                    ) latest ON latest.channel_id=m.channel_id AND latest.id=m.id
                    WHERE m.instance_id=$instance AND m.account_id=$account
                    """;
                Scope(command, scope);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var channelId = Guid.Parse(reader.GetString(0));
                    var messageId = Guid.Parse(reader.GetString(1));
                    var payload = JsonSerializer.Deserialize(reader.GetString(2), ProtocolJson.Default.MessageDto);
                    var author = payload?.AuthorId;
                    if (rows.TryGetValue(channelId, out var existing))
                    {
                        if (existing.LastMessageId is not Guid stored || MessageMarkup.IdAfter(messageId, stored))
                            rows[channelId] = existing with { LastMessageId = messageId, LastAuthorId = author };
                    }
                    else
                        rows[channelId] = new(channelId, null, messageId, author, null, ChannelNotify.All, null);
                }
            }
            return rows.Values.ToList();
        }, cancellationToken);

    public Task NoteArrivalAsync(CacheScope scope, MessageDto message, bool mentioned, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO channel_inbox (instance_id, account_id, channel_id, last_message_id, last_author_id, last_mention_id, notify)
                VALUES ($instance,$account,$channel,$message,$author,$mention,'all')
                ON CONFLICT(instance_id,account_id,channel_id) DO UPDATE SET
                    last_message_id=$message,
                    last_author_id=$author,
                    last_mention_id=CASE WHEN $mentioned=1 THEN $message ELSE last_mention_id END
                """;
            Scope(command, scope);
            command.Parameters.AddWithValue("$channel", message.ChannelId.ToString("D"));
            command.Parameters.AddWithValue("$message", message.Id.ToString("D"));
            command.Parameters.AddWithValue("$author", message.AuthorId.ToString("D"));
            command.Parameters.AddWithValue("$mention", mentioned ? message.Id.ToString("D") : (object)DBNull.Value);
            command.Parameters.AddWithValue("$mentioned", mentioned ? 1 : 0);
            command.ExecuteNonQuery();
        }, cancellationToken);

    public Task SaveReadAsync(CacheScope scope, Guid channelId, Guid lastReadId, CancellationToken cancellationToken) =>
        UpsertInbox(scope, channelId, cancellationToken, command =>
        {
            command.CommandText =
                """
                INSERT INTO channel_inbox (instance_id, account_id, channel_id, last_read_id, notify)
                VALUES ($instance,$account,$channel,$read,'all')
                ON CONFLICT(instance_id,account_id,channel_id) DO UPDATE SET last_read_id=$read
                """;
            command.Parameters.AddWithValue("$read", lastReadId.ToString("D"));
        });

    public Task SaveNotifyAsync(CacheScope scope, Guid channelId, ChannelNotify notify, CancellationToken cancellationToken) =>
        UpsertInbox(scope, channelId, cancellationToken, command =>
        {
            command.CommandText =
                """
                INSERT INTO channel_inbox (instance_id, account_id, channel_id, notify)
                VALUES ($instance,$account,$channel,$notify)
                ON CONFLICT(instance_id,account_id,channel_id) DO UPDATE SET notify=$notify
                """;
            command.Parameters.AddWithValue("$notify", ChannelInbox.NotifyId(notify));
        });

    public Task SaveDraftAsync(CacheScope scope, Guid channelId, string? draft, CancellationToken cancellationToken) =>
        UpsertInbox(scope, channelId, cancellationToken, command =>
        {
            command.CommandText =
                """
                INSERT INTO channel_inbox (instance_id, account_id, channel_id, draft, notify)
                VALUES ($instance,$account,$channel,$draft,'all')
                ON CONFLICT(instance_id,account_id,channel_id) DO UPDATE SET draft=$draft
                """;
            command.Parameters.AddWithValue("$draft", string.IsNullOrEmpty(draft) ? DBNull.Value : draft);
        });

    private Task UpsertInbox(CacheScope scope, Guid channelId, CancellationToken cancellationToken, Action<SqliteCommand> bind) =>
        Task.Run(() =>
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            Scope(command, scope);
            command.Parameters.AddWithValue("$channel", channelId.ToString("D"));
            bind(command);
            command.ExecuteNonQuery();
        }, cancellationToken);

    private static Guid? ReadId(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) || !Guid.TryParse(reader.GetString(ordinal), out var id) ? null : id;
}
