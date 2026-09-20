using Chat.Core.Instances;

namespace Chat.UI.Instances;

public sealed class InstanceItem(InstanceContext context)
{
    public InstanceContext Context { get; } = context;
    public string Name => Context.Descriptor.DisplayName;
    public string Host => Context.Descriptor.BaseUrl.Authority;
    public string Initial => Name.Length == 0 ? "·" : System.Globalization.StringInfo.GetNextTextElement(Name);
}
