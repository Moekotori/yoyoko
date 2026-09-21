using System.Collections.Frozen;
using System.Text;

namespace Chat.Core.Messaging;

internal delegate void MarkupParse(string content, List<MarkupSpan> spans, int depth);

internal static class HtmlMarkup
{
    public const int MaxDepth = 8;
    private const int MaxEntity = 32;
    private const int MaxRows = 12;
    private const int MaxCells = 8;

    private static readonly FrozenDictionary<string, string> Entities =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["amp"] = "&", ["lt"] = "<", ["gt"] = ">", ["quot"] = "\"", ["apos"] = "'",
            ["nbsp"] = "\u00A0", ["ensp"] = "\u2002", ["emsp"] = "\u2003", ["thinsp"] = "\u2009",
            ["ndash"] = "–", ["mdash"] = "—", ["hellip"] = "…", ["middot"] = "·", ["bull"] = "•",
            ["copy"] = "©", ["reg"] = "®", ["trade"] = "™", ["sect"] = "§", ["para"] = "¶",
            ["deg"] = "°", ["plusmn"] = "±", ["times"] = "×", ["divide"] = "÷",
            ["ne"] = "≠", ["le"] = "≤", ["ge"] = "≥", ["infin"] = "∞",
            ["laquo"] = "«", ["raquo"] = "»", ["lsquo"] = "‘", ["rsquo"] = "’",
            ["ldquo"] = "“", ["rdquo"] = "”", ["euro"] = "€", ["pound"] = "£",
            ["yen"] = "¥", ["cent"] = "¢",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static bool LooksLike(string text, int i)
    {
        if ((uint)i >= (uint)text.Length || text[i] != '<') return false;
        if (i + 1 >= text.Length) return false;
        var c = text[i + 1];
        return c is '/' or '!' or '?' || char.IsAsciiLetter(c);
    }

    public static bool LooksLikeEntity(string text, int i)
    {
        if ((uint)i + 2 >= (uint)text.Length || text[i] != '&') return false;
        var c = text[i + 1];
        if (c != '#' && !char.IsAsciiLetter(c)) return false;
        var end = text.IndexOf(';', i + 2, Math.Min(MaxEntity, text.Length - i - 2));
        return end > i + 1;
    }

    public static bool TryEntity(string text, ref int i, out string value)
    {
        value = "";
        if (!TryDecodeEntity(text, i, out value, out var next)) return false;
        i = next;
        return true;
    }

    public static bool TryParse(string text, ref int i, List<MarkupSpan> spans, int depth, MarkupParse parse)
    {
        if (!LooksLike(text, i) || !TryReadTag(text, i, out var tag, out var after)) return false;
        if (tag.IsClose || tag.Kind is HtmlKind.Comment or HtmlKind.Close)
        {
            i = after;
            return true;
        }
        if (tag.Kind == HtmlKind.Break)
        {
            spans.Add(new(MarkupKind.Text, "\n"));
            i = after;
            return true;
        }
        if (tag.Kind == HtmlKind.Rule)
        {
            spans.Add(new(MarkupKind.Rule, ""));
            i = after;
            return true;
        }
        if (tag.Kind == HtmlKind.Image)
        {
            if (!string.IsNullOrEmpty(tag.Alt)) spans.Add(new(MarkupKind.Text, tag.Alt));
            i = after;
            return true;
        }
        if (tag.Void)
        {
            i = after;
            return true;
        }

        var raw = tag.Kind is HtmlKind.Code or HtmlKind.Fence or HtmlKind.Drop;
        if (!FindClose(text, after, tag.Name, raw, out var innerEnd, out var closeAfter))
        {
            if (tag.Kind == HtmlKind.Drop)
            {
                i = after;
                return true;
            }
            return false;
        }
        if (tag.Kind == HtmlKind.Drop)
        {
            i = closeAfter;
            return true;
        }

        var inner = text[after..innerEnd];
        i = closeAfter;
        if (depth >= MaxDepth)
        {
            var flat = VisibleText(inner);
            if (flat.Length > 0) spans.Add(new(MarkupKind.Text, flat));
            return true;
        }
        Emit(tag, inner, spans, depth, parse);
        return true;
    }

    private static void Emit(in HtmlTag tag, string inner, List<MarkupSpan> spans, int depth, MarkupParse parse)
    {
        switch (tag.Kind)
        {
            case HtmlKind.Bold:
                Apply(inner, MarkupKind.Bold, spans, depth, parse);
                return;
            case HtmlKind.Italic:
                Apply(inner, MarkupKind.Italic, spans, depth, parse);
                return;
            case HtmlKind.Underline:
                Apply(inner, MarkupKind.Underline, spans, depth, parse);
                return;
            case HtmlKind.Strike:
                Apply(inner, MarkupKind.Strike, spans, depth, parse);
                return;
            case HtmlKind.Code:
                spans.Add(new(MarkupKind.Code, DecodeText(inner)));
                return;
            case HtmlKind.Fence:
                spans.Add(new(MarkupKind.Fence, DecodeText(Unwrap(inner, "code"))));
                return;
            case HtmlKind.Link:
                if (IsSafeHttp(tag.Href, out var href))
                    spans.Add(new(MarkupKind.Link, VisibleText(inner) is { Length: > 0 } label ? label : href, href));
                else
                    parse(inner, spans, depth + 1);
                return;
            case HtmlKind.Heading:
                spans.Add(new(MarkupKind.Heading, VisibleText(inner).Trim(), tag.Level.ToString()));
                return;
            case HtmlKind.Quote:
                spans.Add(new(MarkupKind.Quote, VisibleText(inner).Trim()));
                return;
            case HtmlKind.Paragraph:
                parse(inner, spans, depth + 1);
                if (spans.Count == 0 || !EndsWithNewline(spans[^1]))
                    spans.Add(new(MarkupKind.Text, "\n"));
                return;
            case HtmlKind.List:
            case HtmlKind.OrderedList:
                EmitList(inner, tag.Kind == HtmlKind.OrderedList, spans);
                return;
            case HtmlKind.Item:
                spans.Add(new(MarkupKind.ListItem, VisibleText(inner).Trim(), ""));
                return;
            case HtmlKind.Table:
                if (!EmitTable(inner, spans)) parse(inner, spans, depth + 1);
                return;
            default:
                parse(inner, spans, depth + 1);
                return;
        }
    }

    private static void Apply(string inner, MarkupKind kind, List<MarkupSpan> spans, int depth, MarkupParse parse)
    {
        var nested = new List<MarkupSpan>(4);
        parse(inner, nested, depth + 1);
        if (nested.Count == 0)
        {
            var text = DecodeText(inner);
            if (text.Length > 0) spans.Add(new(kind, text));
            return;
        }
        foreach (var span in nested)
            spans.Add(span.Kind == MarkupKind.Text ? span with { Kind = kind } : span);
    }

    private static void EmitList(string inner, bool ordered, List<MarkupSpan> spans)
    {
        var index = 1;
        var i = 0;
        var found = false;
        while (i < inner.Length)
        {
            if (!TryReadTag(inner, i, out var tag, out var after) || tag.Kind != HtmlKind.Item || tag.IsClose)
            {
                i = after > i ? after : i + 1;
                continue;
            }
            if (!FindClose(inner, after, tag.Name, raw: false, out var end, out var closeAfter))
                break;
            var extra = ordered ? index.ToString() : "";
            spans.Add(new(MarkupKind.ListItem, VisibleText(inner[after..end]).Trim(), extra));
            index++;
            found = true;
            i = closeAfter;
        }
        if (!found)
        {
            var text = VisibleText(inner).Trim();
            if (text.Length > 0) spans.Add(new(MarkupKind.ListItem, text, ordered ? "1" : ""));
        }
    }

    private static bool EmitTable(string inner, List<MarkupSpan> spans)
    {
        var rows = new List<string>(4);
        var i = 0;
        while (i < inner.Length && rows.Count < MaxRows)
        {
            if (!TryReadTag(inner, i, out var tag, out var after))
            {
                i++;
                continue;
            }
            if (tag.IsClose || tag.Kind is not HtmlKind.Row and not HtmlKind.Unwrap)
            {
                i = after;
                continue;
            }
            if (tag.Kind == HtmlKind.Unwrap)
            {
                i = after;
                continue;
            }
            if (!FindClose(inner, after, tag.Name, raw: false, out var end, out var closeAfter))
                break;
            var cells = SplitCells(inner[after..end]);
            if (cells.Length >= 2) rows.Add(string.Join('\t', cells));
            i = closeAfter;
        }
        if (rows.Count == 0) return false;
        foreach (var row in rows)
            spans.Add(new(MarkupKind.Table, row));
        return true;
    }

    private static string[] SplitCells(string row)
    {
        var cells = new List<string>(4);
        var i = 0;
        while (i < row.Length && cells.Count < MaxCells)
        {
            if (!TryReadTag(row, i, out var tag, out var after) || tag.Kind != HtmlKind.Cell || tag.IsClose)
            {
                i = after > i ? after : i + 1;
                continue;
            }
            if (!FindClose(row, after, tag.Name, raw: false, out var end, out var closeAfter))
                break;
            cells.Add(VisibleText(row[after..end]).Trim());
            i = closeAfter;
        }
        return [.. cells];
    }

    private static bool EndsWithNewline(MarkupSpan span) =>
        span.Text.Length > 0 && span.Text[^1] is '\n' or '\r';

    private static bool IsSafeHttp(string? href, out string url)
    {
        url = href?.Trim() ?? "";
        if (url.Length is < 8 or > 512) return false;
        foreach (var c in url)
            if (c is '\n' or '\r' or ' ' or '\t') return false;
        return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
    }

    private static string Unwrap(string html, string name)
    {
        var i = 0;
        while (i < html.Length && char.IsWhiteSpace(html[i])) i++;
        if (!TryReadTag(html, i, out var tag, out var after)
            || !tag.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || tag.IsClose)
            return html;
        if (!FindClose(html, after, tag.Name, raw: true, out var end, out var closeAfter))
            return html;
        var n = closeAfter;
        while (n < html.Length && char.IsWhiteSpace(html[n])) n++;
        return n == html.Length ? html[after..end] : html;
    }

    private static string VisibleText(string html)
    {
        var text = new StringBuilder(html.Length);
        var i = 0;
        while (i < html.Length)
        {
            if (html[i] == '<' && TryReadTag(html, i, out var tag, out var after))
            {
                if (tag.Kind == HtmlKind.Break) text.Append('\n');
                if (tag.Kind == HtmlKind.Drop && !tag.IsClose && !tag.Void
                    && FindClose(html, after, tag.Name, raw: true, out _, out var closeAfter))
                {
                    i = closeAfter;
                    continue;
                }
                if (tag.Kind == HtmlKind.Image && !string.IsNullOrEmpty(tag.Alt))
                    text.Append(tag.Alt);
                i = after;
                continue;
            }
            if (TryDecodeEntity(html, i, out var value, out var next))
            {
                text.Append(value);
                i = next;
                continue;
            }
            text.Append(html[i++]);
        }
        return text.ToString();
    }

    private static string DecodeText(string text)
    {
        if (text.IndexOf('&') < 0) return text;
        var built = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (TryDecodeEntity(text, i, out var value, out var next))
            {
                built.Append(value);
                i = next;
            }
            else built.Append(text[i++]);
        }
        return built.ToString();
    }

    private static bool TryDecodeEntity(string text, int i, out string value, out int next)
    {
        value = "";
        next = i;
        if ((uint)i + 2 >= (uint)text.Length || text[i] != '&') return false;
        var end = text.IndexOf(';', i + 1, Math.Min(MaxEntity, text.Length - i - 1));
        if (end < 0) return false;
        var body = text.AsSpan(i + 1, end - i - 1);
        if (body.Length == 0) return false;
        if (body[0] == '#')
        {
            if (!TryNumeric(body, out var code) || !TryRune(code, out value)) return false;
            next = end + 1;
            return true;
        }
        foreach (var c in body)
            if (!char.IsAsciiLetterOrDigit(c)) return false;
        if (!Entities.TryGetValue(body.ToString(), out value!)) return false;
        next = end + 1;
        return true;
    }

    private static bool TryNumeric(ReadOnlySpan<char> body, out int code)
    {
        code = 0;
        if (body.Length < 2) return false;
        var hex = body[1] is 'x' or 'X';
        var digits = hex ? body[2..] : body[1..];
        if (digits.Length == 0 || digits.Length > 7) return false;
        var n = 0;
        foreach (var c in digits)
        {
            var d = hex ? Hex(c) : c is >= '0' and <= '9' ? c - '0' : -1;
            if (d < 0) return false;
            n = n * (hex ? 16 : 10) + d;
            if (n > 0x10FFFF) return false;
        }
        code = n;
        return true;
    }

    private static int Hex(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1
    };

    private static bool TryRune(int code, out string value)
    {
        value = "";
        if (code is 0 or > 0x10FFFF) return false;
        if (code < 32 && code is not 9 and not 10 and not 13) return false;
        if (code is >= 0xD800 and <= 0xDFFF) return false;
        value = char.ConvertFromUtf32(code);
        return true;
    }

    private static bool FindClose(string text, int from, string name, bool raw, out int innerEnd, out int after)
    {
        innerEnd = after = from;
        if (raw)
        {
            var needle = "</" + name;
            var i = from;
            while (i < text.Length)
            {
                var at = text.IndexOf(needle, i, StringComparison.OrdinalIgnoreCase);
                if (at < 0) return false;
                var n = at + needle.Length;
                while (n < text.Length && char.IsWhiteSpace(text[n])) n++;
                if (n < text.Length && text[n] == '>')
                {
                    innerEnd = at;
                    after = n + 1;
                    return true;
                }
                i = at + 1;
            }
            return false;
        }

        var depth = 1;
        var scan = from;
        while (scan < text.Length)
        {
            if (text[scan] != '<' || !TryReadTag(text, scan, out var tag, out var next))
            {
                scan++;
                continue;
            }
            if (tag.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && !tag.IsClose && !tag.Void) depth++;
            else if (tag.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && tag.IsClose)
            {
                depth--;
                if (depth == 0)
                {
                    innerEnd = scan;
                    after = next;
                    return true;
                }
            }
            scan = next;
        }
        return false;
    }

    private static bool TryReadTag(string text, int i, out HtmlTag tag, out int after)
    {
        tag = default;
        after = i;
        if ((uint)i >= (uint)text.Length || text[i] != '<') return false;
        if (i + 3 < text.Length && text[i + 1] == '!' && text[i + 2] == '-' && text[i + 3] == '-')
        {
            var end = text.IndexOf("-->", i + 4, StringComparison.Ordinal);
            if (end < 0) return false;
            tag = new HtmlTag(HtmlKind.Comment, "", 0, false, true, null, null);
            after = end + 3;
            return true;
        }
        if (i + 1 < text.Length && text[i + 1] is '!' or '?')
        {
            var end = text.IndexOf('>', i + 2);
            if (end < 0) return false;
            tag = new HtmlTag(HtmlKind.Comment, "", 0, false, true, null, null);
            after = end + 1;
            return true;
        }

        var n = i + 1;
        var close = false;
        if (n < text.Length && text[n] == '/')
        {
            close = true;
            n++;
        }
        var nameStart = n;
        while (n < text.Length && char.IsAsciiLetterOrDigit(text[n])) n++;
        if (n == nameStart || n - nameStart > 16) return false;
        var name = text[nameStart..n];
        string? href = null;
        string? alt = null;
        var self = false;
        while (n < text.Length)
        {
            while (n < text.Length && char.IsWhiteSpace(text[n])) n++;
            if (n >= text.Length) return false;
            if (text[n] == '>') { n++; break; }
            if (text[n] == '/' && n + 1 < text.Length && text[n + 1] == '>')
            {
                self = true;
                n += 2;
                break;
            }
            var keyStart = n;
            while (n < text.Length && (char.IsAsciiLetterOrDigit(text[n]) || text[n] == '-')) n++;
            if (n == keyStart) return false;
            var key = text[keyStart..n];
            while (n < text.Length && char.IsWhiteSpace(text[n])) n++;
            var value = "";
            if (n < text.Length && text[n] == '=')
            {
                n++;
                while (n < text.Length && char.IsWhiteSpace(text[n])) n++;
                if (n >= text.Length) return false;
                if (text[n] is '"' or '\'')
                {
                    var q = text[n++];
                    var start = n;
                    while (n < text.Length && text[n] != q) n++;
                    if (n >= text.Length) return false;
                    value = text[start..n];
                    n++;
                }
                else
                {
                    var start = n;
                    while (n < text.Length && text[n] is not '>' and not '/' && !char.IsWhiteSpace(text[n])) n++;
                    value = text[start..n];
                }
            }
            if (key.Equals("href", StringComparison.OrdinalIgnoreCase)) href = DecodeText(value);
            else if (key.Equals("alt", StringComparison.OrdinalIgnoreCase)) alt = DecodeText(value);
        }
        var kind = Classify(name, close, self);
        tag = new HtmlTag(kind, name, HeadingLevel(name), close, self || IsVoid(kind), href, alt);
        after = n;
        return true;
    }

    private static int HeadingLevel(string name) => name.Length == 2 && name[0] is 'h' or 'H' && name[1] is >= '1' and <= '6'
        ? Math.Min(name[1] - '0', 3)
        : 0;

    private static bool IsVoid(HtmlKind kind) =>
        kind is HtmlKind.Break or HtmlKind.Rule or HtmlKind.Image or HtmlKind.Comment;

    private static HtmlKind Classify(string name, bool close, bool self)
    {
        if (name.Equals("b", StringComparison.OrdinalIgnoreCase)
            || name.Equals("strong", StringComparison.OrdinalIgnoreCase))
            return HtmlKind.Bold;
        if (name.Equals("i", StringComparison.OrdinalIgnoreCase)
            || name.Equals("em", StringComparison.OrdinalIgnoreCase))
            return HtmlKind.Italic;
        if (name.Equals("u", StringComparison.OrdinalIgnoreCase)) return HtmlKind.Underline;
        if (name.Equals("s", StringComparison.OrdinalIgnoreCase)
            || name.Equals("strike", StringComparison.OrdinalIgnoreCase)
            || name.Equals("del", StringComparison.OrdinalIgnoreCase))
            return HtmlKind.Strike;
        if (name.Equals("code", StringComparison.OrdinalIgnoreCase)) return HtmlKind.Code;
        if (name.Equals("pre", StringComparison.OrdinalIgnoreCase)) return HtmlKind.Fence;
        if (name.Equals("a", StringComparison.OrdinalIgnoreCase)) return HtmlKind.Link;
        if (name.Length == 2 && (name[0] is 'h' or 'H') && name[1] is >= '1' and <= '6')
            return HtmlKind.Heading;
        if (name.Equals("blockquote", StringComparison.OrdinalIgnoreCase)) return HtmlKind.Quote;
        if (name.Equals("p", StringComparison.OrdinalIgnoreCase)) return HtmlKind.Paragraph;
        if (name.Equals("ul", StringComparison.OrdinalIgnoreCase)) return HtmlKind.List;
        if (name.Equals("ol", StringComparison.OrdinalIgnoreCase)) return HtmlKind.OrderedList;
        if (name.Equals("li", StringComparison.OrdinalIgnoreCase)) return HtmlKind.Item;
        if (name.Equals("table", StringComparison.OrdinalIgnoreCase)) return HtmlKind.Table;
        if (name.Equals("tr", StringComparison.OrdinalIgnoreCase)) return HtmlKind.Row;
        if (name.Equals("td", StringComparison.OrdinalIgnoreCase)
            || name.Equals("th", StringComparison.OrdinalIgnoreCase))
            return HtmlKind.Cell;
        if (name.Equals("br", StringComparison.OrdinalIgnoreCase)) return HtmlKind.Break;
        if (name.Equals("hr", StringComparison.OrdinalIgnoreCase)) return HtmlKind.Rule;
        if (name.Equals("img", StringComparison.OrdinalIgnoreCase)) return HtmlKind.Image;
        if (IsDropped(name)) return HtmlKind.Drop;
        if (close || self) return HtmlKind.Close;
        return HtmlKind.Unwrap;
    }

    private static bool IsDropped(string name) =>
        name.Equals("script", StringComparison.OrdinalIgnoreCase)
        || name.Equals("style", StringComparison.OrdinalIgnoreCase)
        || name.Equals("iframe", StringComparison.OrdinalIgnoreCase)
        || name.Equals("object", StringComparison.OrdinalIgnoreCase)
        || name.Equals("embed", StringComparison.OrdinalIgnoreCase)
        || name.Equals("form", StringComparison.OrdinalIgnoreCase)
        || name.Equals("input", StringComparison.OrdinalIgnoreCase)
        || name.Equals("textarea", StringComparison.OrdinalIgnoreCase)
        || name.Equals("select", StringComparison.OrdinalIgnoreCase)
        || name.Equals("button", StringComparison.OrdinalIgnoreCase)
        || name.Equals("svg", StringComparison.OrdinalIgnoreCase)
        || name.Equals("math", StringComparison.OrdinalIgnoreCase)
        || name.Equals("link", StringComparison.OrdinalIgnoreCase)
        || name.Equals("meta", StringComparison.OrdinalIgnoreCase)
        || name.Equals("base", StringComparison.OrdinalIgnoreCase)
        || name.Equals("video", StringComparison.OrdinalIgnoreCase)
        || name.Equals("audio", StringComparison.OrdinalIgnoreCase)
        || name.Equals("source", StringComparison.OrdinalIgnoreCase)
        || name.Equals("track", StringComparison.OrdinalIgnoreCase)
        || name.Equals("canvas", StringComparison.OrdinalIgnoreCase)
        || name.Equals("dialog", StringComparison.OrdinalIgnoreCase)
        || name.Equals("template", StringComparison.OrdinalIgnoreCase)
        || name.Equals("noscript", StringComparison.OrdinalIgnoreCase)
        || name.Equals("frame", StringComparison.OrdinalIgnoreCase)
        || name.Equals("frameset", StringComparison.OrdinalIgnoreCase)
        || name.Equals("applet", StringComparison.OrdinalIgnoreCase);

    private enum HtmlKind
    {
        Bold, Italic, Underline, Strike, Code, Fence, Link, Heading, Quote, Paragraph,
        List, OrderedList, Item, Table, Row, Cell, Break, Rule, Image, Unwrap, Drop, Close, Comment
    }

    private readonly struct HtmlTag(
        HtmlKind kind, string name, int level, bool close, bool voidTag, string? href, string? alt)
    {
        public HtmlKind Kind { get; } = kind;
        public string Name { get; } = name;
        public int Level { get; } = level;
        public bool IsClose { get; } = close;
        public bool Void { get; } = voidTag;
        public string? Href { get; } = href;
        public string? Alt { get; } = alt;
    }
}
