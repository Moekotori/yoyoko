namespace Chat.Core;

public sealed class ClientFault(string key, params object[] args) : Exception(key)
{
    public string Key { get; } = key;
    public object[] Args { get; } = args;
}
