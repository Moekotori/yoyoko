using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Chat.Core.Messaging;

namespace Chat.UI.Chat;

public sealed class MessageText : SelectableTextBlock
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MessageText, string?>(nameof(Markdown));

    private bool _spoilersOpen;

    static MessageText()
    {
        MarkdownProperty.Changed.AddClassHandler<MessageText>((control, _) => control.Rebuild());
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!_spoilersOpen && HasSpoiler())
        {
            _spoilersOpen = true;
            Rebuild();
            e.Handled = true;
            return;
        }
        base.OnPointerPressed(e);
    }

    private bool HasSpoiler()
    {
        if (Inlines is null) return false;
        foreach (var inline in Inlines)
            if (inline is Run { Tag: "spoiler" }) return true;
        return false;
    }

    private void Rebuild()
    {
        Inlines?.Clear();
        Inlines ??= [];
        var content = Markdown;
        if (string.IsNullOrEmpty(content))
        {
            Text = "";
            return;
        }
        Text = null;
        foreach (var span in MessageMarkup.Parse(content))
            Inlines.Add(RunFor(span));
    }

    private Inline RunFor(MarkupSpan span)
    {
        var run = new Run(span.Text);
        switch (span.Kind)
        {
            case MarkupKind.Bold:
                run.FontWeight = FontWeight.SemiBold;
                break;
            case MarkupKind.Italic:
                run.FontStyle = FontStyle.Italic;
                break;
            case MarkupKind.Code:
            case MarkupKind.Fence:
                run.FontFamily = new FontFamily("Menlo, SF Mono, Consolas, monospace");
                run.Background = new SolidColorBrush(Color.FromRgb(36, 36, 36));
                break;
            case MarkupKind.Spoiler:
                run.Tag = "spoiler";
                if (_spoilersOpen)
                {
                    run.Background = new SolidColorBrush(Color.FromRgb(48, 48, 48));
                }
                else
                {
                    run.Foreground = new SolidColorBrush(Color.FromRgb(42, 42, 42));
                    run.Background = new SolidColorBrush(Color.FromRgb(42, 42, 42));
                }
                break;
            case MarkupKind.Mention:
                run.Text = "@" + span.Text;
                run.FontWeight = FontWeight.Medium;
                run.Foreground = new SolidColorBrush(Color.FromRgb(210, 168, 140));
                break;
            case MarkupKind.Link:
                run.Foreground = new SolidColorBrush(Color.FromRgb(140, 176, 214));
                run.TextDecorations = TextDecorations.Underline;
                break;
        }
        return run;
    }
}
