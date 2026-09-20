using System.Text.Json;
using Chat.Core.Instances;
using Chat.Core.Messaging;
using Chat.Domain.Instances;
using Chat.Protocol;
using Microsoft.Data.Sqlite;

namespace Chat.Storage;

public sealed class SqliteCache : IInstanceStore, IMessageCache
{
    private readonly string _connectionString;
    public SqliteCache(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }
    // SQLite async APIs execute synchronously. Keep all disk work on a worker thread.
    public Task InitializeAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=3000; PRAGMA user_version;";
        command.ExecuteNonQuery();
        command.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(command.ExecuteScalar());
        if (version > 1) throw new InvalidDataException("Cache schema newer than this client.");
        if (version == 0)
        {
            using var stream = typeof(SqliteCache).Assembly.GetManifestResourceStream("Chat.Storage.Migrations.001_cache.sql")!;
            using var reader = new StreamReader(stream);
            using var transaction = connection.BeginTransaction();
            command.Transaction = transaction;
            command.CommandText = reader.ReadToEnd();
            command.ExecuteNonQuery();
            transaction.Commit();
        }
    }, cancellationToken);

    public Task<IReadOnlyList<InstanceDescriptor>> LoadAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<InstanceDescriptor>>(() =>
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, base_url, display_name FROM instances ORDER BY display_name LIMIT 32";
        using var reader = command.ExecuteReader();
        var result = new List<InstanceDescriptor>();
        while (reader.Read()) result.Add(new(new(Guid.Parse(reader.GetString(0))), new(reader.GetString(1)), reader.GetString(2)));
        return result;
    }, cancellationToken);

    public Task SaveAsync(InstanceDescriptor instance, CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO instances VALUES ($id,$url,$name) ON CONFLICT(id) DO UPDATE SET base_url=$url, display_name=$name";
        command.Parameters.AddWithValue("$id", instance.Id.Value.ToString());
        command.Parameters.AddWithValue("$url", instance.BaseUrl.AbsoluteUri);
        command.Parameters.AddWithValue("$name", instance.DisplayName);
        command.ExecuteNonQuery();
    }, cancellationToken);

    public Task UpsertAsync(CacheScope scope, MessageDto message, CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO messages VALUES ($instance,$account,$channel,$id,$payload) ON CONFLICT(instance_id,account_id,id) DO UPDATE SET payload=$payload";
        Scope(command, scope);
        command.Parameters.AddWithValue("$channel", message.ChannelId.ToString());
        command.Parameters.AddWithValue("$id", message.Id.ToString());
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(message, ProtocolJson.Default.MessageDto));
        command.ExecuteNonQuery();
        // Initial bounded retention: max 1,000 rows per account/channel, expanded in Phase 1.
        command.CommandText = "DELETE FROM messages WHERE instance_id=$instance AND account_id=$account AND channel_id=$channel AND id NOT IN (SELECT id FROM messages WHERE instance_id=$instance AND account_id=$account AND channel_id=$channel ORDER BY id DESC LIMIT 1000)";
        command.ExecuteNonQuery();
        transaction.Commit();
    }, cancellationToken);

    public Task<MessagePage> ReadPageAsync(CacheScope scope, Guid channelId, Guid? before, int limit,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (limit is < 1 or > ProtocolVersion.MaxPageSize) throw new ArgumentOutOfRangeException(nameof(limit));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM messages WHERE instance_id=$instance AND account_id=$account AND channel_id=$channel AND ($before IS NULL OR id < $before) ORDER BY id DESC LIMIT $limit";
        Scope(command, scope);
        command.Parameters.AddWithValue("$channel", channelId.ToString());
        command.Parameters.AddWithValue("$before", (object?)before?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit + 1);
        using var reader = command.ExecuteReader();
        var items = new List<MessageDto>(limit + 1);
        while (reader.Read()) items.Add(JsonSerializer.Deserialize(reader.GetString(0), ProtocolJson.Default.MessageDto)!);
        var hasMore = items.Count > limit;
        if (hasMore) items.RemoveAt(limit);
        return new MessagePage(items, hasMore ? items[^1].Id : null);
    }, cancellationToken);

    public Task PurgeAsync(CacheScope scope, CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM messages WHERE instance_id=$instance AND account_id=$account";
        Scope(command, scope);
        command.ExecuteNonQuery();
    }, cancellationToken);

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=3000; PRAGMA cache_size=-2048;";
        command.ExecuteNonQuery();
        return connection;
    }
    private static void Scope(SqliteCommand command, CacheScope scope)
    {
        command.Parameters.AddWithValue("$instance", scope.InstanceId.Value.ToString());
        command.Parameters.AddWithValue("$account", scope.AccountId.ToString());
    }
}
