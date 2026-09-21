namespace Chat.UI.Markup;

public enum MarkBlockKind : byte { Paragraph, Heading, Quote, List, Code, Math, Rule }

[Flags]
public enum MarkStyle : byte
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Strike = 4,
    Code = 8
}

public enum MarkInlineKind : byte { Text, Break, Math, Link }

public readonly struct MarkInline
{
    public MarkInlineKind Kind { get; init; }
    public MarkStyle Style { get; init; }
    public int Start { get; init; }
    public int Length { get; init; }
    public int HrefStart { get; init; }
    public int HrefLength { get; init; }
    public bool Display { get; init; }
    public MathNode? Math { get; init; }
}

public sealed class MarkBlock
{
    public MarkBlockKind Kind { get; init; }
    public int Level { get; init; }
    public int Start { get; init; }
    public int Length { get; init; }
    public MarkInline[] Inlines { get; init; } = [];
    public MarkBlock[] Items { get; init; } = [];
}

public sealed class MarkDocument
{
    public required string Source { get; init; }
    public required MarkBlock[] Blocks { get; init; }
}

public static class MessageMarkup
{
    public const int MaxBlocks = 48;
    public const int MaxInlines = 256;

    public static bool IsPlain(string text)
    {
        var lineStart = true;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '*' or '_' or '~' or '`' or '$' or '[' or '\\' or '|') return false;
            if (lineStart)
            {
                if (c is '#' or '>') return false;
                if (c is '-' or '+' && i + 1 < text.Length && text[i + 1] == ' ') return false;
                if (c is '-' or '*' or '_' && IsRule(text, i)) return false;
                if (c is >= '0' and <= '9')
                {
                    var j = i;
                    while (j < text.Length && text[j] is >= '0' and <= '9') j++;
                    if (j + 1 < text.Length && text[j] == '.' && text[j + 1] == ' ') return false;
                }
                if ((c is 'h' or 'H') && HasHttp(text, i)) return false;
            }
            else if ((c is 'h' or 'H') && HasHttp(text, i)) return false;
            if (c == '\n') lineStart = true;
            else if (c != '\r') lineStart = false;
        }
        return true;
    }

    public static MarkDocument Parse(string source)
    {
        if (string.IsNullOrEmpty(source))
            return new MarkDocument { Source = source ?? "", Blocks = [] };
        try
        {
            return new Parser(source).Parse();
        }
        catch (Exception)
        {
            return new MarkDocument
            {
                Source = source,
                Blocks = [new MarkBlock { Kind = MarkBlockKind.Paragraph, Start = 0, Length = source.Length, Inlines = [Text(0, source.Length)] }]
            };
        }
    }

    private static MarkInline Text(int start, int length, MarkStyle style = MarkStyle.None) =>
        new() { Kind = MarkInlineKind.Text, Start = start, Length = length, Style = style };

    private static bool HasHttp(string text, int i)
    {
        if (i > 0 && (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] is '_' or '-')) return false;
        return text.AsSpan(i).StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || text.AsSpan(i).StartsWith("http://", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRule(string text, int i)
    {
        if (i > 0 && text[i - 1] != '\n' && text[i - 1] != '\r') return false;
        var c = text[i];
        var n = 0;
        var j = i;
        while (j < text.Length && text[j] == c) { n++; j++; }
        if (n < 3) return false;
        while (j < text.Length && text[j] is ' ' or '\t') j++;
        return j == text.Length || text[j] is '\n' or '\r';
    }

    private sealed class Parser(string s)
    {
        private int _i;
        private readonly int _n = s.Length;

        public MarkDocument Parse()
        {
            var blocks = new List<MarkBlock>(8);
            while (_i < _n && blocks.Count < MaxBlocks)
            {
                SkipBlank();
                if (_i >= _n) break;
                if (TryFence(out var fence)) blocks.Add(fence);
                else if (TryDisplayMath(out var math)) blocks.Add(math);
                else if (TryRule(out var rule)) blocks.Add(rule);
                else if (TryHeading(out var heading)) blocks.Add(heading);
                else if (TryQuote(out var quote)) blocks.Add(quote);
                else if (TryList(out var list)) blocks.Add(list);
                else blocks.Add(Paragraph());
            }
            if (_i < _n && blocks.Count >= MaxBlocks)
                blocks.Add(new MarkBlock { Kind = MarkBlockKind.Paragraph, Start = _i, Length = _n - _i, Inlines = [Text(_i, _n - _i)] });
            return new MarkDocument { Source = s, Blocks = [.. blocks] };
        }

        private void SkipBlank()
        {
            while (_i < _n)
            {
                var c = s[_i];
                if (c is ' ' or '\t' or '\r') { _i++; continue; }
                if (c == '\n') { _i++; continue; }
                break;
            }
        }

        private bool TryFence(out MarkBlock block)
        {
            block = null!;
            if (!AtLine("```")) return false;
            var i = _i + 3;
            while (i < _n && s[i] is ' ' or '\t') i++;
            var langStart = i;
            while (i < _n && s[i] is not '\n' and not '\r') i++;
            var langLength = i - langStart;
            if (i < _n && s[i] == '\r') i++;
            if (i < _n && s[i] == '\n') i++;
            var body = i;
            var close = IndexOfFenceClose(i);
            int length, next;
            if (close < 0)
            {
                length = Math.Min(_n - body, 4096);
                next = _n;
            }
            else
            {
                length = close - body;
                next = close + 3;
                while (next < _n && s[next] is ' ' or '\t') next++;
                if (next < _n && s[next] == '\r') next++;
                if (next < _n && s[next] == '\n') next++;
            }
            while (length > 0 && s[body + length - 1] is '\n' or '\r') length--;
            _i = next;
            block = new MarkBlock { Kind = MarkBlockKind.Code, Start = body, Length = length, Level = langLength };
            return true;
        }

        private int IndexOfFenceClose(int from)
        {
            for (var i = from; i + 2 < _n; i++)
            {
                if (s[i] != '`' || s[i + 1] != '`' || s[i + 2] != '`') continue;
                var line = i;
                while (line > from && s[line - 1] is ' ' or '\t') line--;
                if (line > from && s[line - 1] is not '\n' and not '\r') continue;
                return i;
            }
            return -1;
        }

        private bool TryDisplayMath(out MarkBlock block)
        {
            block = null!;
            if (!AtLine("$$")) return false;
            var body = _i + 2;
            if (body < _n && s[body] == '\r') body++;
            if (body < _n && s[body] == '\n') body++;
            var close = s.IndexOf("$$", body, StringComparison.Ordinal);
            if (close < 0) return false;
            var length = close - body;
            while (length > 0 && s[body + length - 1] is '\n' or '\r' or ' ') length--;
            var next = close + 2;
            if (next < _n && s[next] == '\r') next++;
            if (next < _n && s[next] == '\n') next++;
            _i = next;
            var math = MathMarkup.Parse(s.AsSpan(body, length).ToString());
            block = new MarkBlock
            {
                Kind = MarkBlockKind.Math,
                Start = body,
                Length = length,
                Inlines = [new MarkInline { Kind = MarkInlineKind.Math, Math = math, Display = true, Start = body, Length = length }]
            };
            return true;
        }

        private bool TryRule(out MarkBlock block)
        {
            block = null!;
            if (_i >= _n || !IsRule(s, _i)) return false;
            var c = s[_i];
            while (_i < _n && s[_i] == c) _i++;
            while (_i < _n && s[_i] is ' ' or '\t' or '\r') _i++;
            if (_i < _n && s[_i] == '\n') _i++;
            block = new MarkBlock { Kind = MarkBlockKind.Rule };
            return true;
        }

        private bool TryHeading(out MarkBlock block)
        {
            block = null!;
            if (_i >= _n || s[_i] != '#') return false;
            var level = 0;
            var i = _i;
            while (i < _n && s[i] == '#' && level < 3) { level++; i++; }
            if (level == 0 || i >= _n || s[i] != ' ') return false;
            i++;
            var start = i;
            while (i < _n && s[i] is not '\n' and not '\r') i++;
            var length = i - start;
            while (length > 0 && s[start + length - 1] == ' ') length--;
            _i = i;
            if (_i < _n && s[_i] == '\r') _i++;
            if (_i < _n && s[_i] == '\n') _i++;
            block = new MarkBlock
            {
                Kind = MarkBlockKind.Heading,
                Level = level,
                Start = start,
                Length = length,
                Inlines = Inlines(start, start + length, MarkStyle.None)
            };
            return true;
        }

        private bool TryQuote(out MarkBlock block)
        {
            block = null!;
            if (_i >= _n || s[_i] != '>') return false;
            var parts = new List<MarkInline>(8);
            var start = _i;
            while (_i < _n && s[_i] == '>')
            {
                _i++;
                if (_i < _n && s[_i] == ' ') _i++;
                var line = _i;
                while (_i < _n && s[_i] is not '\n' and not '\r') _i++;
                if (parts.Count > 0) parts.Add(new MarkInline { Kind = MarkInlineKind.Break });
                parts.AddRange(Inlines(line, _i, MarkStyle.None));
                if (_i < _n && s[_i] == '\r') _i++;
                if (_i < _n && s[_i] == '\n') _i++;
                SkipHSpace();
            }
            block = new MarkBlock { Kind = MarkBlockKind.Quote, Start = start, Length = _i - start, Inlines = Cap(parts) };
            return true;
        }

        private bool TryList(out MarkBlock block)
        {
            block = null!;
            if (!ListMarker(s, _i, out var ordered, out var width, out var index)) return false;
            var items = new List<MarkBlock>(4);
            while (_i < _n && items.Count < MaxBlocks && ListMarker(s, _i, out var nextOrdered, out width, out var nextIndex))
            {
                if (items.Count > 0 && nextOrdered != ordered) break;
                _i += width;
                var line = _i;
                while (_i < _n && s[_i] is not '\n' and not '\r') _i++;
                items.Add(new MarkBlock
                {
                    Kind = MarkBlockKind.Paragraph,
                    Level = nextIndex,
                    Start = line,
                    Length = _i - line,
                    Inlines = Inlines(line, _i, MarkStyle.None)
                });
                if (_i < _n && s[_i] == '\r') _i++;
                if (_i < _n && s[_i] == '\n') _i++;
                SkipHSpace();
                if (items.Count == 1) ordered = nextOrdered;
            }
            if (items.Count == 0) return false;
            block = new MarkBlock { Kind = MarkBlockKind.List, Level = ordered ? 1 : 0, Items = [.. items] };
            return true;
        }

        private MarkBlock Paragraph()
        {
            var start = _i;
            while (_i < _n)
            {
                if (s[_i] is '\n' or '\r')
                {
                    var next = _i;
                    if (s[next] == '\r') next++;
                    if (next < _n && s[next] == '\n') next++;
                    if (next >= _n || s[next] is '\n' or '\r') break;
                    if (BlockStart(next)) break;
                    _i = next;
                    continue;
                }
                _i++;
            }
            var end = _i;
            while (end > start && s[end - 1] is ' ' or '\t') end--;
            if (_i < _n && s[_i] == '\r') _i++;
            if (_i < _n && s[_i] == '\n') _i++;
            return new MarkBlock
            {
                Kind = MarkBlockKind.Paragraph,
                Start = start,
                Length = end - start,
                Inlines = Inlines(start, end, MarkStyle.None)
            };
        }

        private bool BlockStart(int i) =>
            At(i, "```") || At(i, "$$") || IsRule(s, i) || HeadingStart(i) || (i < _n && s[i] == '>')
            || ListMarker(s, i, out _, out _, out _);

        private bool HeadingStart(int i)
        {
            var n = 0;
            while (i < _n && s[i] == '#' && n < 3) { n++; i++; }
            return n > 0 && i < _n && s[i] == ' ';
        }

        private bool AtLine(string token) => At(_i, token);

        private bool At(int i, string token)
        {
            if (i + token.Length > _n) return false;
            for (var n = 0; n < token.Length; n++)
                if (s[i + n] != token[n]) return false;
            return true;
        }

        private void SkipHSpace()
        {
            while (_i < _n && s[_i] is ' ' or '\t') _i++;
        }

        private MarkInline[] Inlines(int start, int end, MarkStyle style)
        {
            var list = new List<MarkInline>(8);
            ParseInlines(start, end, style, list);
            return Cap(list);
        }

        private void ParseInlines(int start, int end, MarkStyle style, List<MarkInline> dest)
        {
            var i = start;
            var text = start;
            void Flush(int to)
            {
                if (to > text) dest.Add(Text(text, to - text, style));
            }
            while (i < end && dest.Count < MaxInlines)
            {
                var c = s[i];
                if (c == '\\' && i + 1 < end)
                {
                    Flush(i);
                    dest.Add(Text(i + 1, 1, style));
                    i += 2;
                    text = i;
                    continue;
                }
                if (c == '`' && style == (style & ~MarkStyle.Code) && TakeCode(i, end, out var codeStart, out var codeEnd, out var next))
                {
                    Flush(i);
                    dest.Add(Text(codeStart, codeEnd - codeStart, style | MarkStyle.Code));
                    i = text = next;
                    continue;
                }
                if (c == '$' && TakeMath(i, end, out var mathStart, out var mathEnd, out var display, out next))
                {
                    Flush(i);
                    dest.Add(new MarkInline
                    {
                        Kind = MarkInlineKind.Math,
                        Start = mathStart,
                        Length = mathEnd - mathStart,
                        Display = display,
                        Math = MathMarkup.Parse(s.AsSpan(mathStart, mathEnd - mathStart).ToString())
                    });
                    i = text = next;
                    continue;
                }
                if (c == '[' && TakeLink(i, end, out var labelStart, out var labelEnd, out var hrefStart, out var hrefEnd, out next))
                {
                    Flush(i);
                    dest.Add(new MarkInline
                    {
                        Kind = MarkInlineKind.Link,
                        Style = style,
                        Start = labelStart,
                        Length = labelEnd - labelStart,
                        HrefStart = hrefStart,
                        HrefLength = hrefEnd - hrefStart
                    });
                    i = text = next;
                    continue;
                }
                if ((c is 'h' or 'H') && HasHttp(s, i) && TakeAutolink(i, end, out next))
                {
                    Flush(i);
                    dest.Add(new MarkInline
                    {
                        Kind = MarkInlineKind.Link,
                        Style = style,
                        Start = i,
                        Length = next - i,
                        HrefStart = i,
                        HrefLength = next - i
                    });
                    i = text = next;
                    continue;
                }
                if (c == '~' && i + 1 < end && s[i + 1] == '~' && TakeWrapped(i, end, "~~", out var innerStart, out var innerEnd, out next))
                {
                    Flush(i);
                    ParseInlines(innerStart, innerEnd, style | MarkStyle.Strike, dest);
                    i = text = next;
                    continue;
                }
                if (c == '*' && i + 1 < end && s[i + 1] == '*' && TakeWrapped(i, end, "**", out innerStart, out innerEnd, out next))
                {
                    Flush(i);
                    ParseInlines(innerStart, innerEnd, style | MarkStyle.Bold, dest);
                    i = text = next;
                    continue;
                }
                if (c == '_' && i + 1 < end && s[i + 1] == '_' && !MidWord(i) && TakeWrapped(i, end, "__", out innerStart, out innerEnd, out next))
                {
                    Flush(i);
                    ParseInlines(innerStart, innerEnd, style | MarkStyle.Bold, dest);
                    i = text = next;
                    continue;
                }
                if (c == '*' && TakeWrapped(i, end, "*", out innerStart, out innerEnd, out next))
                {
                    Flush(i);
                    ParseInlines(innerStart, innerEnd, style | MarkStyle.Italic, dest);
                    i = text = next;
                    continue;
                }
                if (c == '_' && !MidWord(i) && TakeWrapped(i, end, "_", out innerStart, out innerEnd, out next)
                    && (next >= end || !char.IsLetterOrDigit(s[next])))
                {
                    Flush(i);
                    ParseInlines(innerStart, innerEnd, style | MarkStyle.Italic, dest);
                    i = text = next;
                    continue;
                }
                if (c is '\n' or '\r')
                {
                    Flush(i);
                    dest.Add(new MarkInline { Kind = MarkInlineKind.Break });
                    if (c == '\r' && i + 1 < end && s[i + 1] == '\n') i++;
                    i++;
                    text = i;
                    continue;
                }
                i++;
            }
            Flush(Math.Min(i, end));
            bool MidWord(int at) => at > start && char.IsLetterOrDigit(s[at - 1]);
        }

        private bool TakeCode(int i, int end, out int bodyStart, out int bodyEnd, out int next)
        {
            bodyStart = bodyEnd = next = 0;
            var ticks = 0;
            while (i + ticks < end && s[i + ticks] == '`') ticks++;
            if (ticks == 0) return false;
            var seek = i + ticks;
            while (seek + ticks <= end)
            {
                var n = 0;
                while (seek + n < end && s[seek + n] == '`') n++;
                if (n == ticks)
                {
                    bodyStart = i + ticks;
                    bodyEnd = seek;
                    next = seek + ticks;
                    return bodyEnd >= bodyStart;
                }
                seek += Math.Max(1, n);
            }
            return false;
        }

        private bool TakeMath(int i, int end, out int bodyStart, out int bodyEnd, out bool display, out int next)
        {
            bodyStart = bodyEnd = next = 0;
            display = false;
            if (i >= end || s[i] != '$') return false;
            display = i + 1 < end && s[i + 1] == '$';
            var open = display ? 2 : 1;
            if (!display && i + 1 < end && char.IsWhiteSpace(s[i + 1])) return false;
            var seek = i + open;
            while (seek + open <= end)
            {
                if (s[seek] == '\\') { seek += 2; continue; }
                if (s[seek] == '$' && (!display || (seek + 1 < end && s[seek + 1] == '$')))
                {
                    if (!display && seek > i + open && char.IsWhiteSpace(s[seek - 1])) return false;
                    bodyStart = i + open;
                    bodyEnd = seek;
                    next = seek + open;
                    return bodyEnd > bodyStart && bodyEnd - bodyStart <= MathMarkup.MaxChars;
                }
                if (s[seek] is '\n' or '\r') return false;
                seek++;
            }
            return false;
        }

        private bool TakeLink(int i, int end, out int labelStart, out int labelEnd, out int hrefStart, out int hrefEnd, out int next)
        {
            labelStart = labelEnd = hrefStart = hrefEnd = next = 0;
            if (i >= end || s[i] != '[') return false;
            var close = i + 1;
            while (close < end && s[close] != ']' && s[close] is not '\n' and not '\r') close++;
            if (close >= end || s[close] != ']' || close + 1 >= end || s[close + 1] != '(') return false;
            var href = close + 2;
            var hrefClose = href;
            while (hrefClose < end && s[hrefClose] != ')' && s[hrefClose] is not '\n' and not '\r' and not ' ') hrefClose++;
            if (hrefClose >= end || s[hrefClose] != ')') return false;
            if (!SafeHref(s.AsSpan(href, hrefClose - href))) return false;
            labelStart = i + 1;
            labelEnd = close;
            hrefStart = href;
            hrefEnd = hrefClose;
            next = hrefClose + 1;
            return true;
        }

        private bool TakeAutolink(int i, int end, out int next)
        {
            next = i;
            while (next < end && s[next] is not ' ' and not '\t' and not '\n' and not '\r' and not '<' and not '>' and not '"' and not '\'')
                next++;
            while (next > i && s[next - 1] is '.' or ',' or ';' or ':' or ')' or ']') next--;
            return next - i > 7 && next - i <= 512;
        }

        private bool TakeWrapped(int i, int end, string open, out int innerStart, out int innerEnd, out int next)
        {
            innerStart = innerEnd = next = 0;
            if (!At(i, open)) return false;
            var seek = i + open.Length;
            if (seek >= end || char.IsWhiteSpace(s[seek])) return false;
            while (seek + open.Length <= end)
            {
                if (s[seek] == '\\') { seek += 2; continue; }
                if (At(seek, open) && seek > i + open.Length && !char.IsWhiteSpace(s[seek - 1]))
                {
                    innerStart = i + open.Length;
                    innerEnd = seek;
                    next = seek + open.Length;
                    return innerEnd > innerStart;
                }
                if (s[seek] is '\n' or '\r') return false;
                seek++;
            }
            return false;
        }

        private static bool SafeHref(ReadOnlySpan<char> href) =>
            href.Length is > 7 and <= 512
            && (href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            && !href.Contains(' ') && !href.Contains('\n');

        private static bool ListMarker(string text, int i, out bool ordered, out int width, out int index)
        {
            ordered = false;
            width = 0;
            index = 1;
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
            while (i < text.Length && text[i] is >= '0' and <= '9' && n < 1_000_000)
            {
                n = n * 10 + (text[i] - '0');
                i++;
            }
            if (i + 1 >= text.Length || text[i] != '.' || text[i + 1] != ' ') return false;
            ordered = true;
            index = n;
            width = i + 2 - start;
            return true;
        }

        private static MarkInline[] Cap(List<MarkInline> list)
        {
            if (list.Count <= MaxInlines) return [.. list];
            return [.. list.Take(MaxInlines)];
        }
    }
}
