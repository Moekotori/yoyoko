using System.Globalization;
using Chat.Localization;

namespace Chat.Core.Instances;

// Client share URL: `{origin}/join/{community_invite}`. Not a server route; join still uses POST /servers/join.
public readonly record struct WorkspaceInvite(string Address, string? CommunityCode)
{
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public bool HasCommunityCode => !string.IsNullOrEmpty(CommunityCode);

    public static WorkspaceInvite Parse(string input, string defaultAddress)
    {
        var raw = input.Trim();
        if (IsShareCode(raw))
        {
            if (string.IsNullOrWhiteSpace(defaultAddress)) throw new ClientFault(TextKey.InvalidInvite);
            return new(WorkspaceAddress.Resolve(defaultAddress, defaultAddress), NormalizeCode(raw));
        }
        string? code = null;
        var tokens = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 2 && IsShareCode(tokens[^1]))
        {
            code = NormalizeCode(tokens[^1]);
            raw = string.Join(' ', tokens[..^1]);
        }
        raw = StripQueryFragment(raw, ref code);
        raw = StripJoinPath(raw, ref code);
        raw = StripVoicePath(raw);
        return new(WorkspaceAddress.Resolve(raw, defaultAddress), code);
    }

    public static string Link(Uri baseUrl, string communityCode)
    {
        if (!IsCommunityCode(communityCode)) throw new ClientFault(TextKey.InvalidInvite);
        return baseUrl.GetLeftPart(UriPartial.Authority) + "/join/" + NormalizeCode(communityCode);
    }

    public static string VoiceLink(Uri baseUrl, Guid channelId)
        => baseUrl.GetLeftPart(UriPartial.Authority) + "/voice/" + channelId.ToString("D");

    public static string CodeFrom(string invite)
    {
        var raw = invite.Trim();
        if (IsCommunityCode(raw)) return NormalizeCode(raw);
        string? code = null;
        var tokens = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 2 && IsShareCode(tokens[^1])) return NormalizeCode(tokens[^1]);
        raw = StripQueryFragment(raw, ref code);
        StripJoinPath(raw, ref code);
        return code ?? invite.Trim().ToUpper(CultureInfo.InvariantCulture);
    }

    public static bool IsShareCode(string value)
    {
        var code = value.Trim().ToUpper(CultureInfo.InvariantCulture);
        if (code.Length != 8) return false;
        foreach (var c in code)
            if (Alphabet.IndexOf(c) < 0) return false;
        return true;
    }

    public static bool IsCommunityCode(string value)
    {
        var code = value.Trim();
        if (code.Length is < 6 or > 16) return false;
        foreach (var c in code)
            if (!char.IsAsciiLetterOrDigit(c)) return false;
        return true;
    }

    private static string StripVoicePath(string raw)
    {
        var idx = raw.IndexOf("/voice/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return raw;
        var after = raw[(idx + 7)..].Trim().Trim('/');
        if (after.Length == 0 || after.Contains('/') || !Guid.TryParse(after, out _)) return raw;
        return raw[..idx];
    }

    private static string StripJoinPath(string raw, ref string? code)
    {
        var idx = raw.IndexOf("/join/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return raw;
        var after = raw[(idx + 6)..].Trim().Trim('/');
        if (after.Length == 0 || after.Contains('/') || !IsCommunityCode(after)) return raw;
        code ??= NormalizeCode(after);
        return raw[..idx];
    }

    private static string StripQueryFragment(string raw, ref string? code)
    {
        var hash = raw.IndexOf('#');
        if (hash >= 0)
        {
            var fragment = Uri.UnescapeDataString(raw[(hash + 1)..]);
            if (IsCommunityCode(fragment))
            {
                code ??= NormalizeCode(fragment);
                raw = raw[..hash];
            }
        }
        var queryAt = raw.IndexOf('?');
        if (queryAt < 0) return raw;
        var query = raw[(queryAt + 1)..];
        raw = raw[..queryAt];
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2) continue;
            if (!pair[0].Equals("invite", StringComparison.OrdinalIgnoreCase)
                && !pair[0].Equals("code", StringComparison.OrdinalIgnoreCase))
                continue;
            var value = Uri.UnescapeDataString(pair[1]);
            if (IsCommunityCode(value)) code ??= NormalizeCode(value);
        }
        return raw;
    }

    private static string NormalizeCode(string value) => value.Trim().ToUpper(CultureInfo.InvariantCulture);
}
