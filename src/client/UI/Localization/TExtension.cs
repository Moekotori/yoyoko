using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Chat.UI.Localization;

public sealed class TExtension : MarkupExtension
{
    public TExtension() { }
    public TExtension(string key) => Key = key;
    public string Key { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding("[" + Key + "]") { Source = I18n.Presenter, Mode = BindingMode.OneWay };
}
