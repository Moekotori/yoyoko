namespace Chat.Core.Messaging;

public enum MarkupKind
{
    Text, Bold, Italic, Code, Fence, Spoiler, Mention, Link, Strike, Math, DisplayMath,
    Heading, Quote, ListItem, Table, Rule
}

public readonly record struct MarkupSpan(MarkupKind Kind, string Text, string? Extra = null);

public static class MessageMarkup
{
    public const int MaxSpans = 256;
    private const int CacheSlots = 96;
    private static readonly object Gate = new();
    private static readonly string?[] CacheKeys = new string?[CacheSlots];
    private static readonly MarkupSpan[][] CacheValues = new MarkupSpan[CacheSlots][];
    private static uint _cacheClock;

    public static bool MentionsUser(string? content, string username)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(username)) return false;
        foreach (var span in Parse(content))
            if (span.Kind == MarkupKind.Mention && span.Text.Equals(username, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    public static bool IdAfter(Guid message, Guid? cursor) =>
        cursor is not Guid read || string.CompareOrdinal(message.ToString("D"), read.ToString("D")) > 0;

    public static bool IsPlain(string? content)
    {
        if (string.IsNullOrEmpty(content)) return true;
        var lineStart = true;
        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (c is '*' or '_' or '~' or '`' or '$' or '[' or '\\') return false;
            if ((c is 'h' or 'H') && StartsUrl(content, i, out _)) return false;
            if (lineStart)
            {
                if (c is '#' or '>' or '|') return false;
                if (c == '@') return false;
                if (c is '-' or '+' && i + 1 < content.Length && content[i + 1] == ' ') return false;
                if (IsRule(content, i) || IsOrderedList(content, i)) return false;
            }
            else if (c == '@') return false;
            if (c == '\n') lineStart = true;
            else if (c != '\r') lineStart = false;
        }
        return true;
    }

    public static IReadOnlyList<MarkupSpan> Parse(string? content)
    {
        if (string.IsNullOrEmpty(content)) return [];
        lock (Gate)
        {
            for (var n = 0; n < CacheSlots; n++)
                if (ReferenceEquals(CacheKeys[n], content) || content.Equals(CacheKeys[n]))
                    return CacheValues[n];
        }
        MarkupSpan[] parsed;
        if (IsPlain(content)) parsed = [new(MarkupKind.Text, content)];
        else
        {
            var spans = ParseCore(content);
            parsed = Merge(spans);
            if (parsed.Length > MaxSpans)
                parsed = [.. parsed.AsSpan(0, MaxSpans)];
        }
        lock (Gate)
        {
            var slot = (int)(_cacheClock++ % CacheSlots);
            CacheKeys[slot] = content;
            CacheValues[slot] = parsed;
        }
        return parsed;
    }

    private static List<MarkupSpan> ParseCore(string content)
    {
        var spans = new List<MarkupSpan>(8);
        var i = 0;
        while (i < content.Length && spans.Count < MaxSpans)
        {
            if (AtLineStart(content, i))
            {
                if (TryWrapped(content, ref i, "```", "```", MarkupKind.Fence, spans)) continue;
                if (TryMath(content, ref i, display: true, spans)) continue;
                if (TryHeading(content, ref i, spans)) continue;
                if (TryRule(content, ref i, spans)) continue;
                if (TryQuote(content, ref i, spans)) continue;
                if (TryList(content, ref i, spans)) continue;
                if (TryTable(content, ref i, spans)) continue;
            }
            if (TryWrapped(content, ref i, "```", "```", MarkupKind.Fence, spans)) continue;
            if (TryMath(content, ref i, display: true, spans)) continue;
            if (TryWrapped(content, ref i, "**", "**", MarkupKind.Bold, spans)) continue;
            if (TryWrapped(content, ref i, "__", "__", MarkupKind.Bold, spans)) continue;
            if (TryWrapped(content, ref i, "~~", "~~", MarkupKind.Strike, spans)) continue;
            if (TryWrapped(content, ref i, "||", "||", MarkupKind.Spoiler, spans)) continue;
            if (TryWrapped(content, ref i, "`", "`", MarkupKind.Code, spans)) continue;
            if (TryMath(content, ref i, display: false, spans)) continue;
            if (TryWrapped(content, ref i, "*", "*", MarkupKind.Italic, spans)) continue;
            if (TryUnderscoreItalic(content, ref i, spans)) continue;
            if (TryMention(content, ref i, spans)) continue;
            if (TryMarkdownLink(content, ref i, spans)) continue;
            if (TryLink(content, ref i, spans)) continue;
            var start = i++;
            while (i < content.Length && !LooksSpecial(content, i) && !BlockStart(content, i)) i++;
            spans.Add(new(MarkupKind.Text, content[start..i]));
        }
        return spans;
    }

    private static bool AtLineStart(string text, int i) =>
        i == 0 || text[i - 1] is '\n' or '\r';

    private static bool BlockStart(string text, int i) =>
        AtLineStart(text, i) && (text[i] is '#' or '>' or '|' || IsRule(text, i) || IsOrderedList(text, i)
            || (text[i] is '-' or '+' && i + 1 < text.Length && text[i + 1] == ' '));

    private static bool TryWrapped(string text, ref int i, string open, string close, MarkupKind kind,
        List<MarkupSpan> spans)
    {
        if (i + open.Length + close.Length > text.Length || !text.AsSpan(i).StartsWith(open)) return false;
        var inner = i + open.Length;
        var end = text.IndexOf(close, inner, StringComparison.Ordinal);
        if (end <= inner) return false;
        spans.Add(new(kind, text[inner..end]));
        i = end + close.Length;
        return true;
    }

    private static bool TryMath(string text, ref int i, bool display, List<MarkupSpan> spans)
    {
        var open = display ? "$$" : "$";
        if (i + open.Length * 2 > text.Length || !text.AsSpan(i).StartsWith(open)) return false;
        var inner = i + open.Length;
        if (!display && char.IsWhiteSpace(text[inner])) return false;
        var end = text.IndexOf(open, inner, StringComparison.Ordinal);
        if (end <= inner || end - inner > MathMarkup.MaxChars) return false;
        if (!display && (text.AsSpan(inner, end - inner).Contains('\n') || char.IsWhiteSpace(text[end - 1])))
            return false;
        var from = inner;
        var to = end;
        while (from < to && char.IsWhiteSpace(text[from])) from++;
        while (to > from && char.IsWhiteSpace(text[to - 1])) to--;
        spans.Add(new(display ? MarkupKind.DisplayMath : MarkupKind.Math, text[from..to]));
        i = end + open.Length;
        return true;
    }

    private static bool TryHeading(string text, ref int i, List<MarkupSpan> spans)
    {
        if (text[i] != '#') return false;
        var level = 0;
        var n = i;
        while (n < text.Length && text[n] == '#' && level < 3) { level++; n++; }
        if (level == 0 || n >= text.Length || text[n] != ' ') return false;
        n++;
        var start = n;
        while (n < text.Length && text[n] is not '\n' and not '\r') n++;
        var end = n;
        while (end > start && text[end - 1] == ' ') end--;
        spans.Add(new(MarkupKind.Heading, text[start..end], level.ToString()));
        i = SkipEol(text, n);
        return true;
    }

    private static bool TryRule(string text, ref int i, List<MarkupSpan> spans)
    {
        if (!IsRule(text, i)) return false;
        var c = text[i];
        while (i < text.Length && text[i] == c) i++;
        while (i < text.Length && text[i] is ' ' or '\t') i++;
        i = SkipEol(text, i);
        spans.Add(new(MarkupKind.Rule, ""));
        return true;
    }

    private static bool TryQuote(string text, ref int i, List<MarkupSpan> spans)
    {
        if (text[i] != '>') return false;
        var first = true;
        while (i < text.Length && AtLineStart(text, i) && text[i] == '>')
        {
            i++;
            if (i < text.Length && text[i] == ' ') i++;
            var start = i;
            while (i < text.Length && text[i] is not '\n' and not '\r') i++;
            if (!first) spans.Add(new(MarkupKind.Text, "\n"));
            spans.Add(new(MarkupKind.Quote, text[start..i]));
            first = false;
            i = SkipEol(text, i);
        }
        return !first;
    }

    private static bool TryList(string text, ref int i, List<MarkupSpan> spans)
    {
        if (!ListMarker(text, i, out var ordered, out var width, out var index)) return false;
        var count = 0;
        while (i < text.Length && count < 32 && ListMarker(text, i, out var nextOrdered, out width, out var nextIndex))
        {
            if (count > 0 && nextOrdered != ordered) break;
            i += width;
            var start = i;
            while (i < text.Length && text[i] is not '\n' and not '\r') i++;
            spans.Add(new(MarkupKind.ListItem, text[start..i], nextOrdered ? nextIndex.ToString() : ""));
            i = SkipEol(text, i);
            count++;
            ordered = nextOrdered;
        }
        return count > 0;
    }

    private static bool TryTable(string text, ref int i, List<MarkupSpan> spans)
    {
        if (text[i] != '|') return false;
        var rows = new List<string>(4);
        var mark = i;
        while (i < text.Length && rows.Count < 12 && AtLineStart(text, i) && text[i] == '|')
        {
            var start = i;
            while (i < text.Length && text[i] is not '\n' and not '\r') i++;
            var line = text[start..i];
            i = SkipEol(text, i);
            if (IsTableSep(line)) continue;
            var cells = SplitRow(line);
            if (cells.Length < 2)
            {
                i = mark;
                return false;
            }
            rows.Add(string.Join('\t', cells));
        }
        if (rows.Count == 0) { i = mark; return false; }
        foreach (var row in rows)
            spans.Add(new(MarkupKind.Table, row));
        return true;
    }

    private static string[] SplitRow(string line)
    {
        var inner = line.AsSpan().Trim();
        if (inner.Length > 0 && inner[0] == '|') inner = inner[1..];
        if (inner.Length > 0 && inner[^1] == '|') inner = inner[..^1];
        var parts = inner.ToString().Split('|', 8);
        for (var n = 0; n < parts.Length; n++)
            parts[n] = parts[n].Trim();
        return parts;
    }

    private static bool IsTableSep(string line)
    {
        foreach (var c in line)
            if (c is not '|' and not '-' and not ':' and not ' ' and not '\t') return false;
        return line.Contains('-');
    }

    private static bool TryUnderscoreItalic(string text, ref int i, List<MarkupSpan> spans)
    {
        if (text[i] != '_') return false;
        if (i > 0 && char.IsLetterOrDigit(text[i - 1])) return false;
        var inner = i + 1;
        var end = text.IndexOf('_', inner);
        if (end <= inner) return false;
        if (end + 1 < text.Length && char.IsLetterOrDigit(text[end + 1])) return false;
        if (char.IsWhiteSpace(text[inner]) || char.IsWhiteSpace(text[end - 1])) return false;
        spans.Add(new(MarkupKind.Italic, text[inner..end]));
        i = end + 1;
        return true;
    }

    private static bool TryMarkdownLink(string text, ref int i, List<MarkupSpan> spans)
    {
        if (text[i] != '[') return false;
        var close = text.IndexOf(']', i + 1);
        if (close < 0 || close + 2 >= text.Length || text[close + 1] != '(') return false;
        var hrefEnd = text.IndexOf(')', close + 2);
        if (hrefEnd < 0) return false;
        var href = text[(close + 2)..hrefEnd];
        if (href.Length is < 8 or > 512) return false;
        if (!href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !href.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return false;
        var label = text[(i + 1)..close];
        spans.Add(new(MarkupKind.Link, label.Length == 0 ? href : label, href));
        i = hrefEnd + 1;
        return true;
    }

    private static bool TryMention(string text, ref int i, List<MarkupSpan> spans)
    {
        if (text[i] != '@') return false;
        if (i > 0 && IsNameChar(text[i - 1])) return false;
        var start = i + 1;
        var end = start;
        while (end < text.Length && IsNameChar(text[end])) end++;
        if (end - start < 2) return false;
        spans.Add(new(MarkupKind.Mention, text[start..end]));
        i = end;
        return true;
    }

    private static bool TryLink(string text, ref int i, List<MarkupSpan> spans)
    {
        if (!StartsUrl(text, i, out var prefix)) return false;
        var end = i + prefix;
        while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] is not '<' and not '>') end++;
        while (end > i + prefix && text[end - 1] is '.' or ',' or ';' or ')' or '!' or '?') end--;
        if (end - i < prefix + 3) return false;
        spans.Add(new(MarkupKind.Link, text[i..end]));
        i = end;
        return true;
    }

    private static bool StartsUrl(string text, int i, out int prefix)
    {
        prefix = 0;
        if (text.AsSpan(i).StartsWith("https://", StringComparison.OrdinalIgnoreCase)) { prefix = 8; return true; }
        if (text.AsSpan(i).StartsWith("http://", StringComparison.OrdinalIgnoreCase)) { prefix = 7; return true; }
        return false;
    }

    private static bool LooksSpecial(string text, int i)
    {
        var c = text[i];
        if (c is '*' or '`' or '|' or '@' or '$' or '~' or '_' or '[') return true;
        return (c is 'h' or 'H') && StartsUrl(text, i, out _);
    }

    private static bool IsNameChar(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '_' or '-';

    private static bool IsRule(string text, int i)
    {
        if (i >= text.Length || text[i] is not '-' and not '*' and not '_') return false;
        var c = text[i];
        var n = 0;
        var j = i;
        while (j < text.Length && text[j] == c) { n++; j++; }
        if (n < 3) return false;
        while (j < text.Length && text[j] is ' ' or '\t') j++;
        return j == text.Length || text[j] is '\n' or '\r';
    }

    private static bool IsOrderedList(string text, int i) =>
        ListMarker(text, i, out var ordered, out _, out _) && ordered;

    private static bool ListMarker(string text, int i, out bool ordered, out int width, out int index)
    {
        ordered = false;
        width = 0;
        index = 1;
        if (!AtLineStart(text, i)) return false;
        var start = i;
        var spaces = 0;
        while (i < text.Length && text[i] == ' ' && spaces < 3) { spaces++; i++; }
        if (i >= text.Length) return false;
        if (text[i] is '-' or '+' or '*' && i + 1 < text.Length && text[i + 1] == ' ')
        {
            width = i + 2 - start;
            return true;
        }
        if (text[i] is < '0' or > '9') return false;
        var n = 0;
        var digits = 0;
        while (i < text.Length && text[i] is >= '0' and <= '9' && digits < 6)
        {
            n = n * 10 + (text[i] - '0');
            i++;
            digits++;
        }
        if (i + 1 >= text.Length || text[i] != '.' || text[i + 1] != ' ') return false;
        ordered = true;
        index = n;
        width = i + 2 - start;
        return true;
    }

    private static int SkipEol(string text, int i)
    {
        if (i < text.Length && text[i] == '\r') i++;
        if (i < text.Length && text[i] == '\n') i++;
        return i;
    }

    private static MarkupSpan[] Merge(List<MarkupSpan> spans)
    {
        if (spans.Count == 0) return [];
        var merged = new List<MarkupSpan>(spans.Count);
        foreach (var span in spans)
        {
            if (span.Kind == MarkupKind.Text && merged.Count > 0 && merged[^1].Kind == MarkupKind.Text)
                merged[^1] = new(MarkupKind.Text, merged[^1].Text + span.Text);
            else
                merged.Add(span);
        }
        return [.. merged];
    }
}
