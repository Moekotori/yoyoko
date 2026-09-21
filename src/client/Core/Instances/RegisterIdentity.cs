namespace Chat.Core.Instances;

// Passwordless first-use names: empty input becomes User; only defaulted names may suffix on conflict.
public readonly record struct RegisterIdentity(string Username, string DisplayName, bool ExplicitUsername)
{
    public const string DefaultName = "User";
    public const int MaxAttempts = 8;

    public static RegisterIdentity From(string? input)
    {
        var trimmed = (input ?? "").Trim();
        if (trimmed.Length == 0)
            return new(DefaultName, DefaultName, false);

        var display = trimmed.Length <= 100 ? trimmed : trimmed[..100];
        return IsUsername(trimmed)
            ? new(trimmed, display, true)
            : new(DefaultName, display, false);
    }

    public string UsernameAt(int attempt)
    {
        if (attempt <= 0) return Username;
        var suffix = (attempt + 1).ToString();
        var max = 64 - suffix.Length;
        var stem = Username.Length <= max ? Username : Username[..max];
        return stem + suffix;
    }

    public static bool IsUsername(string value)
    {
        if (value.Length is < 2 or > 64) return false;
        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-')
                return false;
        }
        return true;
    }
}
