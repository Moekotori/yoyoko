namespace Chat.Core.Voice;

public sealed record AudioCaptureOptions(string Quality, int SampleRateHz, int Channels, int BitrateBps, int FrameMs, bool Dtx, bool Fec);

public static class AudioQualities
{
    public const string Standard = "standard";
    public const string High = "high";
    public const string VeryHigh = "very_high";
    public const string Studio = "studio";
    public static readonly string[] All = [Standard, High, VeryHigh, Studio];

    public static int Rank(string id) => id switch
    {
        Standard => 0,
        High => 1,
        VeryHigh => 2,
        Studio => 3,
        _ => 0
    };

    public static string Clamp(string requested, string max)
    {
        var id = All.Contains(requested) ? requested : Studio;
        return Rank(id) <= Rank(max) ? id : (All.ElementAtOrDefault(Rank(max)) ?? Standard);
    }

    public static AudioCaptureOptions Profile(string id) => Clamp(id, Studio) switch
    {
        Standard => new(Standard, 48000, 1, 64_000, 20, true, true),
        High => new(High, 48000, 2, 128_000, 20, false, true),
        VeryHigh => new(VeryHigh, 48000, 2, 384_000, 20, false, true),
        _ => new(Studio, 48000, 2, 510_000, 20, false, true)
    };

    public static int FrameSamples(string id)
    {
        var profile = Profile(id);
        return profile.SampleRateHz / 1000 * profile.FrameMs * profile.Channels;
    }
}
