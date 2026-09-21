using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Chat.UI.Markup;

public sealed class MessageBody : StackPanel
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MessageBody, string?>(nameof(Text));
    public static readonly StyledProperty<double> EmSizeProperty =
        AvaloniaProperty.Register<MessageBody, double>(nameof(EmSize), 16);

    private static readonly FontFamily Mono = new("Menlo, Consolas, Cascadia Mono, monospace");
    private static readonly SolidColorBrush CodeInk = new(Color.Parse("#D6D6D6"));
    private static readonly SolidColorBrush CodeFill = new(Color.Parse("#2A2A2A"));
    private static readonly SolidColorBrush LinkInk = new(Color.Parse("#8CB4FF"));
    private static readonly SolidColorBrush QuoteEdge = new(Color.Parse("#5A5A5A"));
    private static readonly SolidColorBrush RuleInk = new(Color.Parse("#3A3A3A"));
    private static readonly TextDecorationCollection Strike = [new TextDecoration { Location = TextDecorationLocation.Strikethrough }];

    static MessageBody()
    {
        TextProperty.Changed.AddClassHandler<MessageBody>((body, _) => body.Rebuild());
        EmSizeProperty.Changed.AddClassHandler<MessageBody>((body, _) => body.Rebuild());
    }

    public MessageBody()
    {
        Spacing = 6;
        Orientation = Orientation.Vertical;
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double EmSize
    {
        get => GetValue(EmSizeProperty);
        set => SetValue(EmSizeProperty, value);
    }

    private void Rebuild()
    {
        Children.Clear();
        var text = Text;
        if (string.IsNullOrEmpty(text)) return;
        var em = Math.Clamp(EmSize, 12, 22);
        if (MessageMarkup.IsPlain(text))
        {
            Children.Add(Plain(text, em));
            return;
        }
        foreach (var block in MessageMarkup.Parse(text).Blocks)
            Children.Add(BuildBlock(text, block, em));
    }

    private Control BuildBlock(string source, MarkBlock block, double em) => block.Kind switch
    {
        MarkBlockKind.Heading => Heading(source, block, em),
        MarkBlockKind.Quote => Quote(source, block, em),
        MarkBlockKind.List => ListBlock(source, block, em),
        MarkBlockKind.Code => Fence(source, block, em),
        MarkBlockKind.Math => MathPad(block.Inlines.Length > 0 ? block.Inlines[0].Math : MathMarkup.Parse(Slice(source, block.Start, block.Length)), em, display: true),
        MarkBlockKind.Rule => new Border { Height = 1, Background = RuleInk, Margin = new Thickness(0, 6) },
        _ => Flow(source, block.Inlines, em, "messageText")
    };

    private SelectableTextBlock Heading(string source, MarkBlock block, double em)
    {
        var size = block.Level switch { 1 => em + 6, 2 => em + 3, _ => em + 1 };
        var blockControl = Flow(source, block.Inlines, size, "messageText");
        blockControl.FontWeight = FontWeight.SemiBold;
        return blockControl;
    }

    private Control Quote(string source, MarkBlock block, double em) =>
        new Border
        {
            BorderBrush = QuoteEdge,
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(10, 0, 0, 0),
            Child = Flow(source, block.Inlines, em, "messageText")
        };

    private Control ListBlock(string source, MarkBlock block, double em)
    {
        var column = new StackPanel { Spacing = 3 };
        var ordered = block.Level == 1;
        foreach (var item in block.Items)
        {
            var row = new Grid { ColumnDefinitions = Columns(22, new GridLength(1, GridUnitType.Star)) };
            row.Children.Add(new TextBlock
            {
                Text = ordered ? item.Level + "." : "•",
                FontSize = em,
                Foreground = CodeInk,
                Width = 22
            });
            var body = Flow(source, item.Inlines, em, "messageText");
            Grid.SetColumn(body, 1);
            row.Children.Add(body);
            column.Children.Add(row);
        }
        return column;
    }

    private static ColumnDefinitions Columns(double bullet, GridLength rest)
    {
        var columns = new ColumnDefinitions();
        columns.Add(new ColumnDefinition(bullet, GridUnitType.Pixel));
        columns.Add(new ColumnDefinition(rest));
        return columns;
    }

    private static Control Fence(string source, MarkBlock block, double em) =>
        new Border
        {
            Background = CodeFill,
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(10, 8),
            Child = new SelectableTextBlock
            {
                Text = Slice(source, block.Start, block.Length),
                FontFamily = Mono,
                FontSize = Math.Max(12, em - 2),
                Foreground = CodeInk,
                TextWrapping = TextWrapping.Wrap
            }
        };

    private static Control MathPad(MathNode? node, double em, bool display)
    {
        var view = MathView.Create(node ?? MathNode.Empty, em);
        if (!display) return view;
        return new Border
        {
            Padding = new Thickness(0, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = view
        };
    }

    private static SelectableTextBlock Plain(string text, double em) =>
        new()
        {
            Text = text,
            FontSize = em,
            TextWrapping = TextWrapping.Wrap,
            Classes = { "messageText" }
        };

    private SelectableTextBlock Flow(string source, MarkInline[] inlines, double em, string style)
    {
        var block = new SelectableTextBlock
        {
            FontSize = em,
            TextWrapping = TextWrapping.Wrap,
            Classes = { style }
        };
        if (inlines.Length == 1 && inlines[0].Kind == MarkInlineKind.Text && inlines[0].Style == MarkStyle.None)
        {
            block.Text = Slice(source, inlines[0].Start, inlines[0].Length);
            return block;
        }
        var collection = block.Inlines ??= [];
        foreach (var inline in inlines)
            Add(collection, source, inline, em);
        return block;
    }

    private void Add(InlineCollection collection, string source, MarkInline inline, double em)
    {
        switch (inline.Kind)
        {
            case MarkInlineKind.Break:
                collection.Add(new LineBreak());
                return;
            case MarkInlineKind.Math:
                collection.Add(new InlineUIContainer(MathView.Create(inline.Math ?? MathNode.Empty, inline.Display ? em : em * 0.95)));
                return;
            case MarkInlineKind.Link:
                collection.Add(new InlineUIContainer(LinkButton(Slice(source, inline.Start, inline.Length), Slice(source, inline.HrefStart, inline.HrefLength), em)));
                return;
            default:
                collection.Add(Run(Slice(source, inline.Start, inline.Length), inline.Style));
                return;
        }
    }

    private static Inline Run(string text, MarkStyle style)
    {
        if (style == MarkStyle.None) return new Avalonia.Controls.Documents.Run(text);
        var run = new Span { Inlines = { new Avalonia.Controls.Documents.Run(text) } };
        if (style.HasFlag(MarkStyle.Bold)) run.FontWeight = FontWeight.Bold;
        if (style.HasFlag(MarkStyle.Italic)) run.FontStyle = FontStyle.Italic;
        if (style.HasFlag(MarkStyle.Strike)) run.TextDecorations = Strike;
        if (style.HasFlag(MarkStyle.Code))
        {
            run.FontFamily = Mono;
            run.Foreground = CodeInk;
            run.Background = CodeFill;
        }
        return run;
    }

    private Button LinkButton(string label, string href, double em)
    {
        var button = new Button
        {
            Content = string.IsNullOrEmpty(label) ? href : label,
            Classes = { "messageLink" },
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Foreground = LinkInk,
            FontSize = em,
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Click += (_, _) => Open(href);
        return button;
    }

    private static void Open(string href)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme is not "http" and not "https") return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception) { }
    }

    private static string Slice(string source, int start, int length)
    {
        if (start < 0 || length <= 0 || start >= source.Length) return "";
        var max = Math.Min(length, source.Length - start);
        return source.Substring(start, max);
    }
}
