using System.Collections.Frozen;

namespace Chat.Core.Messaging;

public abstract class MathNode
{
    public static readonly MathNode Empty = new MathText("");
}

public sealed class MathText(string text, bool italic = false) : MathNode
{
    public string Text { get; } = text;
    public bool Italic { get; } = italic;
}

public sealed class MathList(MathNode[] items) : MathNode
{
    public MathNode[] Items { get; } = items;
}

public sealed class MathFrac(MathNode num, MathNode den) : MathNode
{
    public MathNode Num { get; } = num;
    public MathNode Den { get; } = den;
}

public sealed class MathSqrt(MathNode body, MathNode? index) : MathNode
{
    public MathNode Body { get; } = body;
    public MathNode? Index { get; } = index;
}

public sealed class MathScripts(MathNode core, MathNode? super, MathNode? sub) : MathNode
{
    public MathNode Core { get; } = core;
    public MathNode? Super { get; } = super;
    public MathNode? Sub { get; } = sub;
}

public sealed class MathAccent(MathNode body, string mark) : MathNode
{
    public MathNode Body { get; } = body;
    public string Mark { get; } = mark;
}

public sealed class MathMatrix(MathNode[][] rows, string left, string right) : MathNode
{
    public MathNode[][] Rows { get; } = rows;
    public string Left { get; } = left;
    public string Right { get; } = right;
}

public static class MathMarkup
{
    public const int MaxChars = 2048;
    public const int MaxNodes = 64;
    public const int MaxDepth = 8;

    private const int CacheSlots = 48;
    private static readonly object Gate = new();
    private static readonly string?[] CacheKeys = new string?[CacheSlots];
    private static readonly MathNode[] CacheValues = new MathNode[CacheSlots];
    private static uint _cacheClock;

    public static MathNode Parse(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return MathNode.Empty;
        if (source.Length > MaxChars) source = source[..MaxChars];
        lock (Gate)
        {
            for (var n = 0; n < CacheSlots; n++)
                if (ReferenceEquals(CacheKeys[n], source) || source.Equals(CacheKeys[n]))
                    return CacheValues[n];
        }
        MathNode node;
        try
        {
            var parser = new Parser(source);
            node = parser.Expr(stop: null) ?? new MathText(source.Trim());
        }
        catch (Exception)
        {
            node = new MathText(source.Trim());
        }
        lock (Gate)
        {
            var slot = (int)(_cacheClock++ % CacheSlots);
            CacheKeys[slot] = source;
            CacheValues[slot] = node;
        }
        return node;
    }

    public static bool TryFlatten(MathNode node, out string text)
    {
        var buffer = new System.Text.StringBuilder(24);
        if (!Flatten(node, buffer) || buffer.Length == 0)
        {
            text = "";
            return false;
        }
        text = buffer.ToString();
        return true;
    }

    private static bool Flatten(MathNode node, System.Text.StringBuilder buffer)
    {
        switch (node)
        {
            case MathText text:
                buffer.Append(text.Text.AsSpan().Trim());
                return true;
            case MathList list:
                foreach (var item in list.Items)
                    if (!Flatten(item, buffer)) return false;
                return true;
            case MathScripts scripts:
                if (!Flatten(scripts.Core, buffer)) return false;
                if (scripts.Super is not null)
                {
                    if (scripts.Super is not MathText up || !TryScript(up.Text, super: true, out var sup))
                        return false;
                    buffer.Append(sup);
                }
                if (scripts.Sub is not null)
                {
                    if (scripts.Sub is not MathText down || !TryScript(down.Text, super: false, out var sub))
                        return false;
                    buffer.Append(sub);
                }
                return true;
            default:
                return false;
        }
    }

    private static bool TryScript(string text, bool super, out string mapped)
    {
        mapped = "";
        if (string.IsNullOrEmpty(text) || text.Length > 4) return false;
        Span<char> chars = stackalloc char[4];
        for (var i = 0; i < text.Length; i++)
        {
            var next = MapScript(text[i], super);
            if (next is null) return false;
            chars[i] = next.Value;
        }
        mapped = new string(chars[..text.Length]);
        return true;
    }

    private static char? MapScript(char c, bool super) => (c, super) switch
    {
        ('0', true) => '⁰', ('1', true) => '¹', ('2', true) => '²', ('3', true) => '³',
        ('4', true) => '⁴', ('5', true) => '⁵', ('6', true) => '⁶', ('7', true) => '⁷',
        ('8', true) => '⁸', ('9', true) => '⁹', ('+', true) => '⁺', ('-', true) => '⁻',
        ('n', true) => 'ⁿ', ('i', true) => 'ⁱ',
        ('0', false) => '₀', ('1', false) => '₁', ('2', false) => '₂', ('3', false) => '₃',
        ('4', false) => '₄', ('5', false) => '₅', ('6', false) => '₆', ('7', false) => '₇',
        ('8', false) => '₈', ('9', false) => '₉', ('+', false) => '₊', ('-', false) => '₋',
        _ => null
    };

    private sealed class Parser(string s)
    {
        private int _i;
        private int _nodes;
        private int _depth;
        private bool _cell;

        public MathNode? Expr(char? stop)
        {
            if (_depth >= MaxDepth) return RestAsText();
            _depth++;
            var items = new List<MathNode>(8);
            while (_i < s.Length && _nodes < MaxNodes)
            {
                SkipSpace();
                if (_i >= s.Length) break;
                var c = s[_i];
                if (stop is { } end && c == end) break;
                if (c is '}' or '&') break;
                if (c == '\\' && CommandEquals("end")) break;
                if (_cell && c == '\\' && _i + 1 < s.Length && s[_i + 1] == '\\') break;
                var atom = Atom();
                if (atom is null) break;
                items.Add(atom);
            }
            _depth--;
            if (items.Count == 0) return MathNode.Empty;
            return items.Count == 1 ? items[0] : new MathList([.. items]);
        }

        private MathNode? Atom()
        {
            if (_nodes >= MaxNodes || _i >= s.Length) return null;
            _nodes++;
            var core = Primary();
            if (core is null) return null;
            MathNode? super = null;
            MathNode? sub = null;
            while (_i < s.Length)
            {
                SkipSpace();
                if (_i >= s.Length) break;
                if (s[_i] == '^' && super is null)
                {
                    _i++;
                    super = Script();
                    continue;
                }
                if (s[_i] == '_' && sub is null)
                {
                    _i++;
                    sub = Script();
                    continue;
                }
                break;
            }
            if (super is null && sub is null) return core;
            return new MathScripts(core, super, sub);
        }

        private MathNode? Primary()
        {
            SkipSpace();
            if (_i >= s.Length) return null;
            var c = s[_i];
            if (c == '{') return Group();
            if (c == '\\') return Command();
            if (c is '^' or '_') return MathNode.Empty;
            if (c == '}') return null;
            _i++;
            var italic = char.IsLetter(c);
            return new MathText(c.ToString(), italic);
        }

        private MathNode Script()
        {
            SkipSpace();
            if (_i >= s.Length) return MathNode.Empty;
            if (s[_i] == '{') return Group() ?? MathNode.Empty;
            var node = Primary();
            return node ?? MathNode.Empty;
        }

        private MathNode? Group()
        {
            if (_i >= s.Length || s[_i] != '{') return null;
            _i++;
            var inner = Expr('}');
            if (_i < s.Length && s[_i] == '}') _i++;
            return inner ?? MathNode.Empty;
        }

        private MathNode? Command()
        {
            _i++;
            if (_i >= s.Length) return new MathText("\\");
            var c = s[_i];
            if (!char.IsLetter(c))
            {
                _i++;
                return c switch
                {
                    ' ' or ',' => new MathText(" "),
                    ';' or ':' => new MathText("  "),
                    '\\' => new MathText("\n"),
                    '{' or '}' or '_' or '%' or '$' or '#' or '&' => new MathText(c.ToString()),
                    _ => new MathText(c.ToString())
                };
            }
            var start = _i;
            while (_i < s.Length && char.IsLetter(s[_i])) _i++;
            var name = s[start.._i];
            if (name is "left" or "right")
            {
                SkipSpace();
                if (_i < s.Length && s[_i] == '\\') return Command();
                if (_i < s.Length && s[_i] is not '{' and not '^' and not '_')
                {
                    var delim = s[_i].ToString();
                    _i++;
                    return new MathText(delim);
                }
                return Atom();
            }
            if (name is "frac" or "dfrac" or "tfrac")
            {
                var num = Group() ?? Script();
                var den = Group() ?? Script();
                return new MathFrac(num, den);
            }
            if (name == "sqrt")
            {
                SkipSpace();
                MathNode? index = null;
                if (_i < s.Length && s[_i] == '[')
                {
                    _i++;
                    index = Expr(']');
                    if (_i < s.Length && s[_i] == ']') _i++;
                }
                var body = Group() ?? Script();
                return new MathSqrt(body, index);
            }
            if (name is "overline" or "bar") return new MathAccent(Group() ?? Script(), "¯");
            if (name == "vec") return new MathAccent(Group() ?? Script(), "→");
            if (name == "hat") return new MathAccent(Group() ?? Script(), "^");
            if (name == "tilde") return new MathAccent(Group() ?? Script(), "~");
            if (name == "dot") return new MathAccent(Group() ?? Script(), "˙");
            if (name is "text" or "mathrm" or "operatorname" or "mathbf")
                return Upright(Group() ?? Script());
            if (name == "mathbb")
                return Blackboard(Group() ?? Script());
            if (name == "begin") return Environment();
            if (name is "quad") return new MathText("  ");
            if (name is "qquad") return new MathText("    ");
            if (Symbols.TryGetValue(name, out var symbol))
                return new MathText(symbol, italic: false);
            if (Functions.Contains(name))
                return new MathText(name, italic: false);
            return new MathText("\\" + name, italic: false);
        }

        private MathNode Environment()
        {
            SkipSpace();
            if (_i >= s.Length || s[_i] != '{') return new MathText("\\begin");
            _i++;
            var start = _i;
            while (_i < s.Length && char.IsLetter(s[_i])) _i++;
            var env = s[start.._i];
            if (_i < s.Length && s[_i] == '}') _i++;
            var (left, right) = env switch
            {
                "pmatrix" => ("(", ")"),
                "bmatrix" => ("[", "]"),
                "vmatrix" => ("|", "|"),
                "matrix" => ("", ""),
                _ => ("(", ")")
            };
            var rows = new List<MathNode[]>(4);
            var row = new List<MathNode>(4);
            _cell = true;
            try
            {
                while (_i < s.Length && _nodes < MaxNodes && rows.Count < 6)
                {
                    SkipSpace();
                    if (_i >= s.Length) break;
                    if (CommandEquals("end"))
                    {
                        ConsumeEnd();
                        break;
                    }
                    if (s[_i] == '&')
                    {
                        _i++;
                        continue;
                    }
                    if (s[_i] == '\\' && _i + 1 < s.Length && s[_i + 1] == '\\')
                    {
                        _i += 2;
                        rows.Add([.. row]);
                        row.Clear();
                        continue;
                    }
                    var cell = Expr(stop: null);
                    row.Add(cell ?? MathNode.Empty);
                    if (row.Count >= 6)
                    {
                        rows.Add([.. row]);
                        row.Clear();
                    }
                }
                if (row.Count > 0) rows.Add([.. row]);
            }
            finally { _cell = false; }
            if (rows.Count == 0) return new MathText("");
            return new MathMatrix([.. rows], left, right);
        }

        private void ConsumeEnd()
        {
            if (_i < s.Length && s[_i] == '\\') _i++;
            while (_i < s.Length && char.IsLetter(s[_i])) _i++;
            if (_i < s.Length && s[_i] == '{')
            {
                while (_i < s.Length && s[_i] != '}') _i++;
                if (_i < s.Length && s[_i] == '}') _i++;
            }
        }

        private static MathNode Upright(MathNode node) => node switch
        {
            MathText text => new MathText(text.Text, false),
            MathList list => new MathList([.. list.Items.Select(Upright)]),
            _ => node
        };

        private static MathNode Blackboard(MathNode node) => node switch
        {
            MathText text => new MathText(text.Text switch
            {
                "R" or "r" => "ℝ",
                "N" or "n" => "ℕ",
                "Z" or "z" => "ℤ",
                "Q" or "q" => "ℚ",
                "C" or "c" => "ℂ",
                "P" or "p" => "ℙ",
                _ => text.Text
            }, false),
            MathList list => new MathList([.. list.Items.Select(Blackboard)]),
            _ => node
        };

        private bool CommandEquals(string name)
        {
            if (_i + 1 + name.Length > s.Length || s[_i] != '\\') return false;
            for (var n = 0; n < name.Length; n++)
                if (s[_i + 1 + n] != name[n]) return false;
            var after = _i + 1 + name.Length;
            return after == s.Length || !char.IsLetter(s[after]);
        }

        private void SkipSpace()
        {
            while (_i < s.Length && char.IsWhiteSpace(s[_i]) && s[_i] != '\n') _i++;
        }

        private MathNode RestAsText()
        {
            var start = _i;
            _i = s.Length;
            return start < s.Length ? new MathText(s[start..].Trim()) : MathNode.Empty;
        }
    }

    private static readonly FrozenSet<string> Functions = new[]
    {
        "sin", "cos", "tan", "cot", "sec", "csc", "sinh", "cosh", "tanh",
        "log", "ln", "exp", "lim", "sup", "inf", "max", "min", "det", "gcd", "dim", "ker", "Pr"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> Symbols = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ", ["epsilon"] = "ε",
        ["varepsilon"] = "ε", ["zeta"] = "ζ", ["eta"] = "η", ["theta"] = "θ", ["vartheta"] = "ϑ",
        ["iota"] = "ι", ["kappa"] = "κ", ["lambda"] = "λ", ["mu"] = "μ", ["nu"] = "ν",
        ["xi"] = "ξ", ["pi"] = "π", ["varpi"] = "ϖ", ["rho"] = "ρ", ["varrho"] = "ϱ",
        ["sigma"] = "σ", ["varsigma"] = "ς", ["tau"] = "τ", ["upsilon"] = "υ", ["phi"] = "φ",
        ["varphi"] = "ϕ", ["chi"] = "χ", ["psi"] = "ψ", ["omega"] = "ω",
        ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ", ["Lambda"] = "Λ", ["Xi"] = "Ξ",
        ["Pi"] = "Π", ["Sigma"] = "Σ", ["Upsilon"] = "Υ", ["Phi"] = "Φ", ["Psi"] = "Ψ", ["Omega"] = "Ω",
        ["cdot"] = "·", ["times"] = "×", ["ast"] = "*", ["star"] = "⋆", ["pm"] = "±", ["mp"] = "∓",
        ["div"] = "÷", ["oplus"] = "⊕", ["otimes"] = "⊗",
        ["leq"] = "≤", ["le"] = "≤", ["geq"] = "≥", ["ge"] = "≥", ["neq"] = "≠", ["ne"] = "≠",
        ["approx"] = "≈", ["equiv"] = "≡", ["sim"] = "∼", ["simeq"] = "≃", ["propto"] = "∝",
        ["infty"] = "∞", ["partial"] = "∂", ["nabla"] = "∇", ["emptyset"] = "∅", ["hbar"] = "ℏ",
        ["ell"] = "ℓ", ["Re"] = "ℜ", ["Im"] = "ℑ",
        ["in"] = "∈", ["notin"] = "∉", ["subset"] = "⊂", ["supset"] = "⊃",
        ["subseteq"] = "⊆", ["supseteq"] = "⊇", ["cup"] = "∪", ["cap"] = "∩", ["setminus"] = "∖",
        ["to"] = "→", ["rightarrow"] = "→", ["leftarrow"] = "←", ["Rightarrow"] = "⇒",
        ["Leftarrow"] = "⇐", ["leftrightarrow"] = "↔", ["mapsto"] = "↦",
        ["ldots"] = "…", ["cdots"] = "⋯", ["vdots"] = "⋮",
        ["sum"] = "∑", ["prod"] = "∏", ["int"] = "∫", ["oint"] = "∮",
        ["bigcup"] = "⋃", ["bigcap"] = "⋂",
        ["dots"] = "…", ["prime"] = "′", ["degree"] = "°",
        ["forall"] = "∀", ["exists"] = "∃", ["neg"] = "¬", ["land"] = "∧", ["lor"] = "∨",
        ["wedge"] = "∧", ["vee"] = "∨", ["perp"] = "⊥", ["parallel"] = "∥",
        ["langle"] = "⟨", ["rangle"] = "⟩", ["vert"] = "|", ["Vert"] = "∥"
    }.ToFrozenDictionary(StringComparer.Ordinal);
}
