using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Chat.Core.Messaging;
using MathView = Chat.UI.Markup.MathView;

namespace Chat.UI.Chat;

public sealed class MessageText : SelectableTextBlock
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MessageText, string?>(nameof(Markdown));

    private static readonly AttachedProperty<bool> SpoilerProperty =
        AvaloniaProperty.RegisterAttached<MessageText, Run, bool>("Spoiler");
    private static readonly FontFamily Mono = new("Menlo, SF Mono, Consolas, monospace");
    private static readonly SolidColorBrush CodeFill = new(Color.FromRgb(36, 36, 36));
    private static readonly SolidColorBrush SpoilerOpen = new(Color.FromRgb(48, 48, 48));
    private static readonly SolidColorBrush SpoilerClosed = new(Color.FromRgb(42, 42, 42));
    private static readonly SolidColorBrush MentionInk = new(Color.FromRgb(210, 168, 140));
    private static readonly SolidColorBrush LinkInk = new(Color.FromRgb(140, 176, 214));
    private bool _spoilersOpen;

    static MessageText()
    {
        MarkdownProperty.Changed.AddClassHandler<MessageText>((control, _) =>
        {
            control._spoilersOpen = false;
            control.Rebuild();
        });
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
            if (inline is Run run && run.GetValue(SpoilerProperty)) return true;
        return false;
    }

    private void Rebuild()
    {
        var content = Markdown;
        if (string.IsNullOrEmpty(content))
        {
            Inlines?.Clear();
            Text = "";
            return;
        }
        var spans = global::Chat.Core.Messaging.MessageMarkup.Parse(content);
        if (spans.Count == 1 && spans[0].Kind == MarkupKind.Text)
        {
            Inlines?.Clear();
            Text = spans[0].Text;
            return;
        }
        Inlines ??= [];
        Inlines.Clear();
        Text = null;
        var em = FontSize > 1 ? FontSize : 16;
        foreach (var span in spans)
        {
            if (span.Kind is MarkupKind.Math or MarkupKind.DisplayMath)
            {
                if (span.Kind == MarkupKind.DisplayMath && Inlines.Count > 0)
                    Inlines.Add(new LineBreak());
                Inlines.Add(new InlineUIContainer(MathView.Create(global::Chat.Core.Messaging.MathMarkup.Parse(span.Text),
                    span.Kind == MarkupKind.DisplayMath ? em : em * 0.95)));
                if (span.Kind == MarkupKind.DisplayMath)
                    Inlines.Add(new LineBreak());
                continue;
            }
            Inlines.Add(RunFor(span));
        }
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
            case MarkupKind.Strike:
                run.TextDecorations = [new TextDecoration { Location = TextDecorationLocation.Strikethrough }];
                break;
            case MarkupKind.Code:
            case MarkupKind.Fence:
                run.FontFamily = Mono;
                run.Background = CodeFill;
                break;
            case MarkupKind.Spoiler:
                run.SetValue(SpoilerProperty, true);
                if (_spoilersOpen) run.Background = SpoilerOpen;
                else
                {
                    run.Foreground = SpoilerClosed;
                    run.Background = SpoilerClosed;
                }
                break;
            case MarkupKind.Mention:
                run.Text = "@" + span.Text;
                run.FontWeight = FontWeight.Medium;
                run.Foreground = MentionInk;
                break;
            case MarkupKind.Link:
                run.Foreground = LinkInk;
                run.TextDecorations = [new TextDecoration { Location = TextDecorationLocation.Underline }];
                break;
        }
        return run;
    }
}
