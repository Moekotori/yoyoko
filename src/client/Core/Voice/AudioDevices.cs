namespace Chat.Core.Voice;

public sealed record AudioDevice(string Id, string Name);
public sealed record AudioDeviceList(IReadOnlyList<AudioDevice> Inputs, IReadOnlyList<AudioDevice> Outputs);
public sealed record AudioRoute(string? InputDeviceId, string? OutputDeviceId)
{
    public static AudioRoute System { get; } = new(null, null);
}

public interface IVoiceDevicePreference
{
    string? InputDeviceId { get; }
    string? OutputDeviceId { get; }
    void SetDevices(string? inputId, string? outputId);
}
