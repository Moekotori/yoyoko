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

public static class MathMarkup
{
    public const int MaxChars = 2048;
    public const int MaxNodes = 64;
    public const int MaxDepth = 8;

    public static MathNode Parse(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return MathNode.Empty;
        if (source.Length > MaxChars) source = source[..MaxChars];
        try
        {
            var parser = new Parser(source);
            var node = parser.Expr(stop: null);
            return node ?? new MathText(source.Trim());
        }
        catch (Exception)
        {
            return new MathText(source.Trim());
        }
    }

    private sealed class Parser(string s)
    {
        private int _i;
        private int _nodes;
        private int _depth;

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
            if (name is "quad") return new MathText("  ");
            if (name is "qquad") return new MathText("    ");
            if (Symbols.TryGetValue(name, out var symbol))
                return new MathText(symbol, italic: false);
            if (Functions.Contains(name))
                return new MathText(name, italic: false);
            return new MathText("\\" + name, italic: false);
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
