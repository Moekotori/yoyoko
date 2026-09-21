namespace Chat.UI.Shortcuts;

public interface IShortcutPreference
{
    IReadOnlyDictionary<string, string> Overrides { get; }
    void Assign(string action, string? chord);
    void Reset();
    event Action? Changed;
}

public sealed class MemoryShortcutPreference : IShortcutPreference
{
    private readonly Dictionary<string, string> _overrides = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> Overrides => _overrides;
    public event Action? Changed;
    public void Assign(string action, string? chord)
    {
        if (string.IsNullOrWhiteSpace(action)) return;
        if (chord is null)
        {
            if (!_overrides.Remove(action)) return;
        }
        else if (_overrides.TryGetValue(action, out var current) && current == chord) return;
        else _overrides[action] = chord;
        Changed?.Invoke();
    }
    public void Reset()
    {
        if (_overrides.Count == 0) return;
        _overrides.Clear();
        Changed?.Invoke();
    }
}
