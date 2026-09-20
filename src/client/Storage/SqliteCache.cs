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
        if (version > 2) throw new InvalidDataException("Cache schema newer than this client.");
        if (version < 1) Apply(connection, "Chat.Storage.Migrations.001_cache.sql");
        if (version < 2) Apply(connection, "Chat.Storage.Migrations.002_sync.sql");
    }, cancellationToken);

    private static void Apply(SqliteConnection connection, string resource)
    {
        using var stream = typeof(SqliteCache).Assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = reader.ReadToEnd();
        command.ExecuteNonQuery();
        transaction.Commit();
    }

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
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        Scope(command, scope);
        foreach (var table in new[] { "messages", "servers", "channels", "users", "sync_state" })
        {
            command.CommandText = $"DELETE FROM {table} WHERE instance_id=$instance AND account_id=$account";
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }, cancellationToken);

    public Task SaveCommunityAsync(CacheScope scope, CommunitySnapshot snapshot, CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        Scope(command, scope);
        foreach (var table in new[] { "servers", "channels", "users" })
        {
            command.CommandText = $"DELETE FROM {table} WHERE instance_id=$instance AND account_id=$account";
            command.ExecuteNonQuery();
        }
        foreach (var server in snapshot.Servers)
        {
            command.CommandText = "INSERT INTO servers VALUES ($instance,$account,$id,$payload)";
            command.Parameters.Clear();
            Scope(command, scope);
            command.Parameters.AddWithValue("$id", server.Id.ToString());
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(server, ProtocolJson.Default.ServerDto));
            command.ExecuteNonQuery();
        }
        foreach (var channel in snapshot.Channels)
        {
            command.CommandText = "INSERT INTO channels VALUES ($instance,$account,$id,$server,$payload)";
            command.Parameters.Clear();
            Scope(command, scope);
            command.Parameters.AddWithValue("$id", channel.Id.ToString());
            command.Parameters.AddWithValue("$server", channel.ServerId.ToString());
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(channel, ProtocolJson.Default.ChannelDto));
            command.ExecuteNonQuery();
        }
        foreach (var user in snapshot.Users)
        {
            command.CommandText = "INSERT INTO users VALUES ($instance,$account,$id,$payload)";
            command.Parameters.Clear();
            Scope(command, scope);
            command.Parameters.AddWithValue("$id", user.Id.ToString());
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(user, ProtocolJson.Default.UserDto));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }, cancellationToken);

    public Task<CommunitySnapshot> LoadCommunityAsync(CacheScope scope, CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var connection = Open();
        return new CommunitySnapshot(ReadAll(connection, scope, "servers", ProtocolJson.Default.ServerDto),
            ReadAll(connection, scope, "channels", ProtocolJson.Default.ChannelDto),
            ReadAll(connection, scope, "users", ProtocolJson.Default.UserDto));
    }, cancellationToken);

    public Task SaveCursorAsync(CacheScope scope, string? sessionId, long seq, CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO sync_state VALUES ($instance,$account,$session,$seq) ON CONFLICT(instance_id,account_id) DO UPDATE SET session_id=$session, last_committed_seq=$seq";
        Scope(command, scope);
        command.Parameters.AddWithValue("$session", (object?)sessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$seq", seq);
        command.ExecuteNonQuery();
    }, cancellationToken);

    public Task<(string? SessionId, long Seq)> LoadCursorAsync(CacheScope scope, CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT session_id, last_committed_seq FROM sync_state WHERE instance_id=$instance AND account_id=$account";
        Scope(command, scope);
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetInt64(1)) : ((string?)null, 0L);
    }, cancellationToken);

    public Task SaveAccountAsync(CacheScope scope, string displayName, CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO accounts VALUES ($instance,$id,$name) ON CONFLICT(instance_id,id) DO UPDATE SET display_name=$name";
        command.Parameters.AddWithValue("$instance", scope.InstanceId.Value.ToString());
        command.Parameters.AddWithValue("$id", scope.AccountId.ToString());
        command.Parameters.AddWithValue("$name", displayName);
        command.ExecuteNonQuery();
    }, cancellationToken);

    public Task SetSettingAsync(string key, string value, CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO settings VALUES ($key,$value) ON CONFLICT(key) DO UPDATE SET value=$value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }, cancellationToken);

    public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key=$key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }, cancellationToken);

    private static List<T> ReadAll<T>(SqliteConnection connection, CacheScope scope, string table, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT payload FROM {table} WHERE instance_id=$instance AND account_id=$account";
        Scope(command, scope);
        using var reader = command.ExecuteReader();
        var items = new List<T>();
        while (reader.Read()) items.Add(JsonSerializer.Deserialize(reader.GetString(0), info)!);
        return items;
    }

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
