using System.Diagnostics;
using System.Text.Json;
using Chat.Core.Voice;

namespace Chat.Media;

public sealed class WorkerMediaService : IMediaService
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private int _nextId = 1;
    private bool _session;
    private bool _loopback;
    public WorkerMediaService(string path) => _path = path;
    public MediaCapabilities Capabilities => MediaCapabilities.Voice;
    public bool Available => true;
    public event Action<string>? Faulted;

    public static IMediaService Create()
    {
        var path = Locate();
        return path is null ? new UnavailableMediaService() : new WorkerMediaService(path);
    }

    public async Task ConnectAsync(Uri endpoint, string token, bool muted, bool deafened, AudioCaptureOptions audio, AudioRoute route, CancellationToken cancellationToken)
    {
        await EnsureProcessAsync(cancellationToken);
        _loopback = false;
        _session = true;
        await SendDiscardAsync(new Dictionary<string, object?>
        {
            ["id"] = _nextId++,
            ["op"] = "join",
            ["url"] = endpoint.AbsoluteUri,
            ["token"] = token,
            ["muted"] = muted,
            ["deafened"] = deafened,
            ["quality"] = audio.Quality,
            ["sample_rate_hz"] = audio.SampleRateHz,
            ["channels"] = audio.Channels,
            ["bitrate_bps"] = audio.BitrateBps,
            ["frame_ms"] = audio.FrameMs,
            ["dtx"] = audio.Dtx,
            ["fec"] = audio.Fec,
            ["input_device"] = route.InputDeviceId,
            ["output_device"] = route.OutputDeviceId
        }, cancellationToken);
    }

    public Task SetMutedAsync(bool muted, CancellationToken cancellationToken)
        => SendDiscardAsync(new Dictionary<string, object?> { ["id"] = _nextId++, ["op"] = "mute", ["muted"] = muted }, cancellationToken);
    public Task SetDeafenedAsync(bool deafened, CancellationToken cancellationToken)
        => SendDiscardAsync(new Dictionary<string, object?> { ["id"] = _nextId++, ["op"] = "deafen", ["deafened"] = deafened }, cancellationToken);
    public Task SetQualityAsync(AudioCaptureOptions audio, CancellationToken cancellationToken)
        => SendDiscardAsync(new Dictionary<string, object?>
        {
            ["id"] = _nextId++,
            ["op"] = "quality",
            ["quality"] = audio.Quality,
            ["sample_rate_hz"] = audio.SampleRateHz,
            ["channels"] = audio.Channels,
            ["bitrate_bps"] = audio.BitrateBps,
            ["frame_ms"] = audio.FrameMs,
            ["dtx"] = audio.Dtx,
            ["fec"] = audio.Fec
        }, cancellationToken);

    public async Task<AudioDeviceList> ListDevicesAsync(CancellationToken cancellationToken)
    {
        var owned = await EnsureProcessAsync(cancellationToken);
        try
        {
            using var doc = await SendAsync(new Dictionary<string, object?> { ["id"] = _nextId++, ["op"] = "devices" }, cancellationToken);
            return new AudioDeviceList(ReadDevices(doc.RootElement, "inputs"), ReadDevices(doc.RootElement, "outputs"));
        }
        finally
        {
            if (owned && !_session) await StopAsync();
        }
    }

    public async Task SetDevicesAsync(AudioRoute route, CancellationToken cancellationToken)
    {
        if (_process is null || _process.HasExited)
            return;
        await SendDiscardAsync(new Dictionary<string, object?>
        {
            ["id"] = _nextId++,
            ["op"] = "device",
            ["input_device"] = route.InputDeviceId,
            ["output_device"] = route.OutputDeviceId
        }, cancellationToken);
    }

    public async Task StartLoopbackAsync(AudioCaptureOptions audio, AudioRoute route, CancellationToken cancellationToken)
    {
        await EnsureProcessAsync(cancellationToken);
        await SendDiscardAsync(new Dictionary<string, object?>
        {
            ["id"] = _nextId++,
            ["op"] = "loopback",
            ["channels"] = audio.Channels,
            ["sample_rate_hz"] = audio.SampleRateHz,
            ["frame_ms"] = audio.FrameMs,
            ["input_device"] = route.InputDeviceId,
            ["output_device"] = route.OutputDeviceId
        }, cancellationToken);
        _loopback = true;
    }

    public async Task StopLoopbackAsync(CancellationToken cancellationToken)
    {
        if (!_loopback && (_process is null || _process.HasExited))
            return;
        _loopback = false;
        if (_process is { HasExited: false })
        {
            try { await SendDiscardAsync(new Dictionary<string, object?> { ["id"] = _nextId++, ["op"] = "loopback_stop" }, cancellationToken); }
            catch (Exception) { }
        }
        if (!_session) await StopAsync();
    }

    public async Task LeaveAsync(CancellationToken cancellationToken)
    {
        _session = false;
        _loopback = false;
        await StopAsync();
    }

    private async Task<bool> EnsureProcessAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false }) return false;
        await StopAsync();
        cancellationToken.ThrowIfCancellationRequested();
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
        return true;
    }

    private async Task StopAsync()
    {
        var process = _process;
        _process = null;
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                try
                {
                    await process.StandardInput.WriteLineAsync("{\"id\":0,\"op\":\"leave\"}");
                    await process.StandardInput.FlushAsync();
                }
                catch (Exception) { }
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception) { }
        process.Dispose();
    }

    private async Task SendDiscardAsync(Dictionary<string, object?> payload, CancellationToken cancellationToken)
    {
        using (await SendAsync(payload, cancellationToken)) { }
    }

    private async Task<JsonDocument> SendAsync(Dictionary<string, object?> payload, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var process = _process ?? throw new InvalidOperationException("Media worker is not running.");
            var json = JsonSerializer.Serialize(payload);
            await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Media worker closed.");
                var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("event", out _))
                {
                    doc.Dispose();
                    continue;
                }
                if (doc.RootElement.TryGetProperty("ok", out var ok) && !ok.GetBoolean())
                {
                    var message = doc.RootElement.TryGetProperty("error", out var error)
                        ? error.GetString() ?? "Media worker failed." : "Media worker failed.";
                    doc.Dispose();
                    throw new InvalidOperationException(message);
                }
                return doc;
            }
        }
        finally { _gate.Release(); }
    }

    private static AudioDevice[] ReadDevices(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            return [];
        var items = new List<AudioDevice>();
        foreach (var element in array.EnumerateArray())
        {
            var id = element.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var label = element.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(label)) continue;
            items.Add(new(id, label));
        }
        return [.. items];
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
