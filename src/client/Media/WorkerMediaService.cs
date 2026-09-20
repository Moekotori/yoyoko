using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Chat.Core.Voice;

namespace Chat.Media;

public sealed class WorkerMediaService : IMediaService
{
    private readonly string _path;
    private Process? _process;
    private int _nextId = 1;
    public WorkerMediaService(string path) => _path = path;
    public MediaCapabilities Capabilities => MediaCapabilities.Voice;
    public bool Available => true;
    public event Action<string>? Faulted;

    public static IMediaService Create()
    {
        var path = Locate();
        return path is null ? new UnavailableMediaService() : new WorkerMediaService(path);
    }

    public async Task ConnectAsync(Uri endpoint, string token, bool muted, bool deafened, CancellationToken cancellationToken)
    {
        await LeaveAsync(cancellationToken);
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(_path)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };
        if (!process.Start()) throw new InvalidOperationException("Failed to start media worker.");
        _process = process;
        process.Exited += (_, _) => Faulted?.Invoke("Media worker exited.");
        _ = DrainAsync(process, cancellationToken);
        await SendAsync(new Dictionary<string, object?>
        {
            ["id"] = _nextId++,
            ["op"] = "join",
            ["url"] = endpoint.AbsoluteUri,
            ["token"] = token,
            ["muted"] = muted,
            ["deafened"] = deafened
        }, cancellationToken);
    }

    public Task SetMutedAsync(bool muted, CancellationToken cancellationToken)
        => SendAsync(new Dictionary<string, object?> { ["id"] = _nextId++, ["op"] = "mute", ["muted"] = muted }, cancellationToken);
    public Task SetDeafenedAsync(bool deafened, CancellationToken cancellationToken)
        => SendAsync(new Dictionary<string, object?> { ["id"] = _nextId++, ["op"] = "deafen", ["deafened"] = deafened }, cancellationToken);

    public async Task LeaveAsync(CancellationToken cancellationToken)
    {
        var process = _process;
        _process = null;
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                await process.StandardInput.WriteLineAsync("{\"id\":0,\"op\":\"leave\"}");
                await process.StandardInput.FlushAsync();
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception) { }
        process.Dispose();
    }

    private async Task SendAsync(Dictionary<string, object?> payload, CancellationToken cancellationToken)
    {
        var process = _process ?? throw new InvalidOperationException("Media worker is not running.");
        var json = JsonSerializer.Serialize(payload);
        await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
        var line = await process.StandardOutput.ReadLineAsync(cancellationToken)
            ?? throw new InvalidOperationException("Media worker closed.");
        using var doc = JsonDocument.Parse(line);
        if (doc.RootElement.TryGetProperty("ok", out var ok) && !ok.GetBoolean())
            throw new InvalidOperationException(doc.RootElement.TryGetProperty("error", out var error)
                ? error.GetString() ?? "Media worker failed." : "Media worker failed.");
    }

    private static async Task DrainAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (line is null) break;
            }
        }
        catch (Exception) { }
    }

    private static string? Locate()
    {
        var env = Environment.GetEnvironmentVariable("CHAT_MEDIA_WORKER");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        var names = OperatingSystem.IsWindows() ? new[] { "chat-media-worker.exe", "chat-media-worker" } : new[] { "chat-media-worker" };
        var roots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            Path.Combine(Directory.GetCurrentDirectory(), "target", "release"),
            Path.Combine(Directory.GetCurrentDirectory(), "target", "debug")
        };
        foreach (var root in roots)
        foreach (var name in names)
        {
            var path = Path.Combine(root, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public async ValueTask DisposeAsync() => await LeaveAsync(CancellationToken.None);
}
