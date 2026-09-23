using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Chat.Core.Messaging;
using MathView = Chat.UI.Markup.MathView;

namespace Chat.UI.Chat;

public sealed class MessageText : SelectableTextBlock
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MessageText, string?>(nameof(Markdown));
    public static readonly StyledProperty<string?> SelfUsernameProperty =
        AvaloniaProperty.Register<MessageText, string?>(nameof(SelfUsername));
    public static readonly StyledProperty<string?> SelfDisplayNameProperty =
        AvaloniaProperty.Register<MessageText, string?>(nameof(SelfDisplayName));

    private static readonly AttachedProperty<bool> SpoilerProperty =
        AvaloniaProperty.RegisterAttached<MessageText, Run, bool>("Spoiler");
    private static readonly FontFamily Mono = new("Menlo, SF Mono, Consolas, monospace");
    private static readonly FontFamily MathFont = new("Georgia, Times New Roman, Songti SC, STIX Two Math, serif");
    private bool _spoilersOpen;
    private string? _built;
    private bool _builtSpoilers;

    static MessageText()
    {
        MarkdownProperty.Changed.AddClassHandler<MessageText>((control, _) =>
        {
            control._spoilersOpen = false;
            control.Rebuild();
        });
        SelfUsernameProperty.Changed.AddClassHandler<MessageText>((control, _) =>
        {
            control._built = null;
            control.Rebuild();
        });
        SelfDisplayNameProperty.Changed.AddClassHandler<MessageText>((control, _) =>
        {
            control._built = null;
            control.Rebuild();
        });
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public string? SelfUsername
    {
        get => GetValue(SelfUsernameProperty);
        set => SetValue(SelfUsernameProperty, value);
    }

    public string? SelfDisplayName
    {
        get => GetValue(SelfDisplayNameProperty);
        set => SetValue(SelfDisplayNameProperty, value);
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
        var content = Markdown ?? "";
        if (content == _built && _spoilersOpen == _builtSpoilers) return;
        if (content.Length == 0)
        {
            Inlines?.Clear();
            Text = "";
            _built = content;
            _builtSpoilers = _spoilersOpen;
            return;
        }
        var spans = MessageMarkup.Parse(content);
        if (spans.Count == 1 && spans[0].Kind == MarkupKind.Text)
        {
            Inlines?.Clear();
            Text = spans[0].Text;
            _built = content;
            _builtSpoilers = _spoilersOpen;
            return;
        }
        Inlines ??= [];
        Inlines.Clear();
        Text = null;
        var em = FontSize > 1 ? FontSize : 16;
        foreach (var rawSpan in spans)
        {
            var span = rawSpan;
            if (span.Kind is MarkupKind.Math or MarkupKind.DisplayMath)
            {
                AddMath(span, em);
                continue;
            }
            if (span.Kind is MarkupKind.Heading or MarkupKind.ListItem or MarkupKind.Table or MarkupKind.Rule)
            {
                if (Inlines.Count > 0 && Inlines[^1] is Run previous)
                    previous.Text = previous.Text?.TrimEnd('\r', '\n');
                if (Inlines.Count > 0 && Inlines[^1] is not LineBreak) Inlines.Add(new LineBreak());
                Inlines.Add(InlineFor(span, em));
                Inlines.Add(new LineBreak());
                continue;
            }
            if (Inlines.Count > 0 && Inlines[^1] is LineBreak && span.Kind == MarkupKind.Text)
            {
                var text = span.Text.TrimStart('\r', '\n');
                if (text.Length == 0) continue;
                span = new MarkupSpan(span.Kind, text, span.Extra);
            }
            Inlines.Add(InlineFor(span, em));
        }
        if (Inlines.Count > 0 && Inlines[^1] is LineBreak) Inlines.RemoveAt(Inlines.Count - 1);
        _built = content;
        _builtSpoilers = _spoilersOpen;
    }

    private void AddMath(MarkupSpan span, double em)
    {
        var display = span.Kind == MarkupKind.DisplayMath;
        if (display && Inlines!.Count > 0) Inlines.Add(new LineBreak());
        var tree = MathMarkup.Parse(span.Text);
        if (MathMarkup.TryFlatten(tree, out var flat))
        {
            Inlines!.Add(new Run(flat)
            {
                FontFamily = MathFont,
                FontStyle = FontStyle.Italic,
                FontSize = display ? em + 1 : em
            });
        }
        else
            Inlines!.Add(new InlineUIContainer(MathView.Create(tree, display ? em : em * 0.95)));
        if (display) Inlines.Add(new LineBreak());
    }

    private Inline InlineFor(MarkupSpan span, double em) =>
        span.Kind == MarkupKind.Mention ? MentionChip(span.Text, em) : RunFor(span, em);

    private Inline MentionChip(string token, double em)
    {
        var reserved = MessageMarkup.IsReserved(token);
        var self = !reserved && (
            !string.IsNullOrEmpty(SelfUsername) && token.Equals(SelfUsername, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(SelfDisplayName) && token.Equals(SelfDisplayName, StringComparison.OrdinalIgnoreCase));
        var button = new Button
        {
            Content = "@" + token,
            Tag = token,
            Padding = new Thickness(4, 0),
            MinHeight = 0,
            MinWidth = 0,
            FontSize = em,
            FontWeight = FontWeight.Medium,
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = reserved ? Cursor.Default : new Cursor(StandardCursorType.Hand)
        };
        button.Classes.Add("mentionChip");
        if (reserved || self) button.Classes.Add("strong");
        return new InlineUIContainer(button);
    }

    private Inline RunFor(MarkupSpan span, double em)
    {
        var run = new Run(span.Kind switch
        {
            MarkupKind.ListItem => (string.IsNullOrEmpty(span.Extra) ? "• " : span.Extra + ". ") + span.Text,
            MarkupKind.Table => span.Text.Replace('\t', ' '),
            MarkupKind.Rule => "────────",
            MarkupKind.Quote => span.Text,
            _ => span.Text
        });
        switch (span.Kind)
        {
            case MarkupKind.Bold:
                run.FontWeight = FontWeight.SemiBold;
                break;
            case MarkupKind.Italic:
                run.FontStyle = FontStyle.Italic;
                break;
            case MarkupKind.Strike:
                run.TextDecorations = Avalonia.Media.TextDecorations.Strikethrough;
                break;
            case MarkupKind.Underline:
                run.TextDecorations = Avalonia.Media.TextDecorations.Underline;
                break;
            case MarkupKind.Code:
            case MarkupKind.Fence:
            case MarkupKind.Table:
                run.FontFamily = Mono;
                if (span.Kind != MarkupKind.Table) run.Bind(TextElement.BackgroundProperty, new DynamicResourceExtension("CodeBrush"));
                break;
            case MarkupKind.Spoiler:
                run.SetValue(SpoilerProperty, true);
                if (_spoilersOpen) run.Bind(TextElement.BackgroundProperty, new DynamicResourceExtension("SpoilerOpenBrush"));
                else
                {
                    run.Bind(TextElement.ForegroundProperty, new DynamicResourceExtension("SpoilerClosedBrush"));
                    run.Bind(TextElement.BackgroundProperty, new DynamicResourceExtension("SpoilerClosedBrush"));
                }
                break;
            case MarkupKind.Link:
                run.Bind(TextElement.ForegroundProperty, new DynamicResourceExtension("LinkBrush"));
                run.TextDecorations = Avalonia.Media.TextDecorations.Underline;
                break;
            case MarkupKind.Heading:
                run.FontWeight = FontWeight.SemiBold;
                run.FontSize = em + (span.Extra == "1" ? 6 : span.Extra == "2" ? 3 : 1);
                break;
            case MarkupKind.Quote:
                run.Bind(TextElement.ForegroundProperty, new DynamicResourceExtension("QuoteBrush"));
                break;
            case MarkupKind.Rule:
                run.Bind(TextElement.ForegroundProperty, new DynamicResourceExtension("QuoteBrush"));
                break;
        }
        return run;
    }
}
