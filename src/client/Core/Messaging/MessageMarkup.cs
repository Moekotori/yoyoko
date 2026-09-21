namespace Chat.Core.Messaging;

public enum MarkupKind { Text, Bold, Italic, Code, Fence, Spoiler, Mention, Link }

public readonly record struct MarkupSpan(MarkupKind Kind, string Text);

public static class MessageMarkup
{
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

    public static IReadOnlyList<MarkupSpan> Parse(string? content)
    {
        if (string.IsNullOrEmpty(content)) return [];
        var spans = new List<MarkupSpan>();
        var i = 0;
        while (i < content.Length)
        {
            if (TryWrapped(content, ref i, "```", "```", MarkupKind.Fence, spans)) continue;
            if (TryWrapped(content, ref i, "**", "**", MarkupKind.Bold, spans)) continue;
            if (TryWrapped(content, ref i, "||", "||", MarkupKind.Spoiler, spans)) continue;
            if (TryWrapped(content, ref i, "`", "`", MarkupKind.Code, spans)) continue;
            if (TryWrapped(content, ref i, "*", "*", MarkupKind.Italic, spans)) continue;
            if (TryMention(content, ref i, spans)) continue;
            if (TryLink(content, ref i, spans)) continue;
            var start = i++;
            while (i < content.Length && !LooksSpecial(content, i)) i++;
            spans.Add(new(MarkupKind.Text, content[start..i]));
        }
        return Merge(spans);
    }

    private static bool TryWrapped(string text, ref int i, string open, string close, MarkupKind kind,
        List<MarkupSpan> spans)
    {
        if (i + open.Length + close.Length > text.Length || !text.AsSpan(i).StartsWith(open)) return false;
        var inner = i + open.Length;
        var end = text.IndexOf(close, inner, StringComparison.Ordinal);
        if (end < 0) return false;
        spans.Add(new(kind, text[inner..end]));
        i = end + close.Length;
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

    private static bool LooksSpecial(string text, int i) =>
        text[i] is '*' or '`' or '|' or '@'
        || StartsUrl(text, i, out _);

    private static bool IsNameChar(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '_' or '-';

    private static List<MarkupSpan> Merge(List<MarkupSpan> spans)
    {
        var merged = new List<MarkupSpan>(spans.Count);
        foreach (var span in spans)
        {
            if (span.Kind == MarkupKind.Text && merged.Count > 0 && merged[^1].Kind == MarkupKind.Text)
                merged[^1] = new(MarkupKind.Text, merged[^1].Text + span.Text);
            else
                merged.Add(span);
        }
        return merged;
    }
}
