using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace Chat.UI.Localization;

public sealed class TExtension : MarkupExtension
{
    public TExtension() { }
    public TExtension(string key) => Key = key;
    public string Key { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(I18n.Revision))
        {
            Source = I18n.Presenter,
            Mode = BindingMode.OneWay,
            Converter = TConverter.Shared,
            ConverterParameter = Key
        };
}

internal sealed class TConverter : IValueConverter
{
    public static TConverter Shared { get; } = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        parameter is string key ? I18n.T(key) : "";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
