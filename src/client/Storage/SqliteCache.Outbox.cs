using System.Text.Json;
using Chat.Core.Messaging;
using Microsoft.Data.Sqlite;

namespace Chat.Storage;

public sealed partial class SqliteCache
{
    public Task SaveOutboxAsync(CacheScope scope, OutboxRecord record, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO pending_sends VALUES ($instance,$account,$channel,$id,$payload)
                ON CONFLICT(instance_id,account_id,local_id) DO UPDATE SET channel_id=$channel, payload=$payload
                """;
            Scope(command, scope);
            command.Parameters.AddWithValue("$channel", record.ChannelId.ToString("D"));
            command.Parameters.AddWithValue("$id", record.LocalId.ToString("D"));
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(record));
            command.ExecuteNonQuery();
        }, cancellationToken);

    public Task RemoveOutboxAsync(CacheScope scope, Guid localId, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "DELETE FROM pending_sends WHERE instance_id=$instance AND account_id=$account AND local_id=$id";
            Scope(command, scope);
            command.Parameters.AddWithValue("$id", localId.ToString("D"));
            command.ExecuteNonQuery();
        }, cancellationToken);

    public Task<IReadOnlyList<OutboxRecord>> LoadOutboxAsync(CacheScope scope, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<OutboxRecord>>(() =>
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT payload FROM pending_sends WHERE instance_id=$instance AND account_id=$account ORDER BY local_id";
            Scope(command, scope);
            using var reader = command.ExecuteReader();
            var items = new List<OutboxRecord>();
            while (reader.Read())
            {
                var record = JsonSerializer.Deserialize<OutboxRecord>(reader.GetString(0));
                if (record is not null) items.Add(record);
            }
            return items;
        }, cancellationToken);

    public Task<int> CountOutboxAsync(CacheScope scope, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM pending_sends WHERE instance_id=$instance AND account_id=$account";
            Scope(command, scope);
            return Convert.ToInt32(command.ExecuteScalar());
        }, cancellationToken);

    public Task<string> WriteOutboxFileAsync(CacheScope scope, Guid localId, int index, string fileName, Stream content,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        var directory = OutboxFolder(scope, localId);
        Directory.CreateDirectory(directory);
        var safe = string.Concat(fileName.Where(ch => ch > 31 && !Path.GetInvalidFileNameChars().Contains(ch)));
        if (safe.Length == 0) safe = "file";
        var path = Path.Combine(directory, index.ToString("D2") + "_" + safe);
        using var file = File.Create(path);
        if (content.CanSeek) content.Position = 0;
        content.CopyTo(file);
        return path;
    }, cancellationToken);

    public Task DeleteOutboxFilesAsync(CacheScope scope, Guid localId, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var directory = OutboxFolder(scope, localId);
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }, cancellationToken);

    private string OutboxFolder(CacheScope scope, Guid localId) =>
        Path.Combine(_directory, "outbox", scope.InstanceId.Value.ToString("N"), scope.AccountId.ToString("N"),
            localId.ToString("N"));

    private void DeleteOutboxRoot(CacheScope scope)
    {
        var directory = Path.Combine(_directory, "outbox", scope.InstanceId.Value.ToString("N"),
            scope.AccountId.ToString("N"));
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}
