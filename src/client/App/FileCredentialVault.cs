using Chat.Core.Instances;
using Chat.Domain.Instances;

namespace Chat.App;

// Interim 0600 file store. OS Keychain / Credential Manager adapters remain later.
internal sealed class FileCredentialVault : ICredentialVault
{
    private readonly string _path;
    public FileCredentialVault(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "credentials");
    }

    public Task<string?> GetAsync(InstanceId instance, Guid accountId, CancellationToken cancellationToken) =>
        Task.Run(() => Read().TryGetValue(Key(instance, accountId), out var token) ? token : null, cancellationToken);

    public Task StoreAsync(InstanceId instance, Guid accountId, string refreshToken, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var data = Read();
            data[Key(instance, accountId)] = refreshToken;
            Write(data);
        }, cancellationToken);

    public Task RemoveAsync(InstanceId instance, Guid accountId, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var data = Read();
            data.Remove(Key(instance, accountId));
            Write(data);
        }, cancellationToken);

    private Dictionary<string, string> Read()
    {
        var result = new Dictionary<string, string>();
        if (!File.Exists(_path)) return result;
        foreach (var line in File.ReadAllLines(_path))
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2) result[parts[0]] = parts[1];
        }
        return result;
    }

    private void Write(Dictionary<string, string> data)
    {
        File.WriteAllLines(_path, data.Select(item => item.Key + "=" + item.Value));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static string Key(InstanceId instance, Guid accountId) => instance.Value + ":" + accountId;
}
