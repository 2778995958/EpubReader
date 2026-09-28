using System.Globalization;
using System.Text.RegularExpressions;

namespace EpubReader.Core;

public sealed class ComputedStyle
{
    public WritingMode? WritingMode { get; set; }
    public float FontSizePx { get; set; } = 26;
    public string FontFamily { get; set; } = "Yu Mincho";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public float IndentEm { get; set; }
    public float MarginBeforeEm { get; set; }
    public float MarginAfterEm { get; set; }
    public float PaddingTopEm { get; set; }
    public float PadEndEm { get; set; }
    public float LetterSpacingEm { get; set; }
    public TextAlign Align { get; set; } = TextAlign.Start;
    public bool DisplayNone { get; set; }
    public bool PageBreakBefore { get; set; }
    public bool PageBreakAfter { get; set; }
    public bool CombineUpright { get; set; }
    public EmphasisKind Emphasis { get; set; }
    public int HeadingLevel { get; set; }
    public uint ColorArgb { get; set; } = 0xFF2A2118;
    public float WidthEm { get; set; }
    public float HeightEm { get; set; }

    public ComputedStyle Clone() => (ComputedStyle)MemberwiseClone();

    public TextStyle ToTextStyle(float fontScale) => new()
    {
        FontSizePx = FontSizePx * fontScale,
        FontFamily = FontFamily,
        Bold = Bold,
        Italic = Italic,
        Emphasis = Emphasis,
        ColorArgb = ColorArgb,
        LetterSpacingEm = LetterSpacingEm
    };
}

public sealed class CssSubset
{
    readonly List<(Selector Sel, Dictionary<string, string> Props)> _rules = [];

    public static CssSubset Parse(string css)
    {
        var sheet = new CssSubset();
        css = Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
        foreach (Match m in Regex.Matches(css, @"([^{}]+)\{([^{}]+)\}"))
        {
            var selText = m.Groups[1].Value.Trim();
            if (selText.StartsWith('@')) continue;
            var props = ParseProps(m.Groups[2].Value);
            foreach (var sel in selText.Split(','))
            {
                var parsed = Selector.TryParse(sel.Trim());
                if (parsed != null)
                    sheet._rules.Add((parsed, props));
            }
        }
        return sheet;
    }

    public static Dictionary<string, string> ParseProps(string body)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in body.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = part.IndexOf(':');
            if (i <= 0) continue;
            var key = Canonical(part[..i].Trim());
            var val = part[(i + 1)..].Trim();
            d[key] = val;
        }
        return d;
    }

    public ComputedStyle Apply(
        ComputedStyle parent,
        string tag,
        string? id,
        IEnumerable<string> classes,
        string? inlineStyle,
        IReadOnlyList<(string Tag, string? Id, string[] Classes)>? ancestors = null)
    {
        var s = parent.Clone();
        s.PageBreakBefore = false;
        s.PageBreakAfter = false;
        s.CombineUpright = false;
        s.HeadingLevel = 0;
        s.PaddingTopEm = 0; // padding 不繼承，由 DOM 走訪累加
        s.PadEndEm = 0;
        s.WidthEm = 0;
        s.HeightEm = 0;
        if (tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
        {
            int level = tag[1] - '0';
            ApplyHeadingUa(s, parent.FontSizePx, level);
        }
        if (tag is "em" or "i") s.Italic = true;
        if (tag is "strong" or "b") s.Bold = true;

        foreach (var cls in classes)
            DenshokyoClasses.Apply(s, cls);

        var classList = classes as IList<string> ?? classes.ToList();
        ancestors ??= [];
        foreach (var (sel, props) in _rules)
        {
            if (sel.Matches(tag, id, classList, s.WritingMode ?? parent.WritingMode, ancestors))
                ApplyProps(s, parent, props);
        }
        if (!string.IsNullOrWhiteSpace(inlineStyle))
            ApplyProps(s, parent, ParseProps(inlineStyle));

        // 電書常把 h1 { margin: 0 } 重設掉；閱讀器仍保證標題前後有留白。
        if (s.HeadingLevel > 0)
        {
            var (before, after) = HeadingUaMargins(s.HeadingLevel);
            s.MarginBeforeEm = Math.Max(s.MarginBeforeEm, before);
            s.MarginAfterEm = Math.Max(s.MarginAfterEm, after);
            s.IndentEm = 0;
            s.Bold = true;
        }
        return s;
    }

    internal static void ApplyHeadingUa(ComputedStyle s, float parentPx, int level)
    {
        s.HeadingLevel = level;
        s.Bold = true;
        s.IndentEm = 0;
        s.FontSizePx = parentPx * HeadingSizeEm(level);
        var (before, after) = HeadingUaMargins(level);
        s.MarginBeforeEm = before;
        s.MarginAfterEm = after;
    }

    public static float HeadingSizeEm(int level) => level switch
    {
        1 => 1.6f,
        2 => 1.4f,
        3 => 1.25f,
        _ => 1.1f
    };

    public static (float Before, float After) HeadingUaMargins(int level) => level switch
    {
        1 => (1.6f, 1.1f),
        2 => (1.25f, 0.8f),
        3 => (1.0f, 0.6f),
        _ => (0.8f, 0.45f)
    };

    static void ApplyProps(ComputedStyle s, ComputedStyle parent, Dictionary<string, string> props)
    {
        foreach (var (k, vRaw) in props)
        {
            var v = vRaw.Trim();
            switch (k)
            {
                case "display":
                    s.DisplayNone = v.Equals("none", StringComparison.OrdinalIgnoreCase);
                    break;
                case "writing-mode":
                    if (v.Contains("vertical", StringComparison.OrdinalIgnoreCase)
                        || v.Contains("tb-rl", StringComparison.OrdinalIgnoreCase)
                        || v.Contains("tb-lr", StringComparison.OrdinalIgnoreCase))
                        s.WritingMode = WritingMode.Vertical;
                    else if (v.Contains("horizontal", StringComparison.OrdinalIgnoreCase)
                             || v.Contains("lr-tb", StringComparison.OrdinalIgnoreCase))
                        s.WritingMode = WritingMode.Horizontal;
                    break;
                case "font-size":
                    s.FontSizePx = ParseFontSize(v, parent.FontSizePx);
                    break;
                case "font-family":
                    s.FontFamily = DenshokyoClasses.MapFamily(v.Split(',')[0]);
                    break;
                case "font-weight":
                    s.Bold = v is "bold" or "700" or "800" or "900" or "bolder";
                    break;
                case "font-style":
                    s.Italic = v.Equals("italic", StringComparison.OrdinalIgnoreCase) || v.Equals("oblique", StringComparison.OrdinalIgnoreCase);
                    break;
                case "text-indent":
                    s.IndentEm = IsInherit(v) ? parent.IndentEm : ParseEm(v, parent.FontSizePx);
                    break;
                case "letter-spacing":
                    s.LetterSpacingEm = IsInherit(v) ? parent.LetterSpacingEm : ParseEm(v, parent.FontSizePx);
                    break;
                case "padding-top":
                    s.PaddingTopEm = Math.Max(0, ParseEm(v, parent.FontSizePx));
                    break;
                case "padding-bottom":
                    if ((s.WritingMode ?? parent.WritingMode) == WritingMode.Vertical)
                        s.PadEndEm = Math.Max(s.PadEndEm, Math.Max(0, ParseEm(v, parent.FontSizePx)));
                    else
                        s.MarginAfterEm = ParseEm(v, parent.FontSizePx);
                    break;
                case "padding-left":
                    if ((s.WritingMode ?? parent.WritingMode) != WritingMode.Vertical)
                        s.PaddingTopEm = Math.Max(s.PaddingTopEm, Math.Max(0, ParseEm(v, parent.FontSizePx)));
                    break;
                case "padding-right":
                    if ((s.WritingMode ?? parent.WritingMode) == WritingMode.Vertical)
                        s.MarginBeforeEm = ParseEm(v, parent.FontSizePx);
                    else
                        s.PadEndEm = Math.Max(s.PadEndEm, Math.Max(0, ParseEm(v, parent.FontSizePx)));
                    break;
                case "padding":
                {
                    var pad = v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (pad.Length >= 1)
                        s.PaddingTopEm = Math.Max(0, ParseEm(pad[0], parent.FontSizePx));
                    if (pad.Length >= 3)
                        s.PadEndEm = Math.Max(s.PadEndEm, Math.Max(0, ParseEm(pad[2], parent.FontSizePx)));
                    else if (pad.Length == 1)
                        s.PadEndEm = Math.Max(s.PadEndEm, s.PaddingTopEm);
                    break;
                }
                case "margin-top":
                case "margin-block-start":
                    // 直排物理 top＝行頭；電書協 .vrtl .start-2em { margin-top } 是字下げ。
                    if ((s.WritingMode ?? parent.WritingMode) == WritingMode.Vertical)
                        s.PaddingTopEm = Math.Max(s.PaddingTopEm, Math.Max(0, ParseEm(v, parent.FontSizePx)));
                    else
                        s.MarginBeforeEm = ParseEm(v, parent.FontSizePx);
                    break;
                case "margin-left":
                    if ((s.WritingMode ?? parent.WritingMode) != WritingMode.Vertical)
                        s.IndentEm = ParseEm(v, parent.FontSizePx);
                    else
                        s.MarginAfterEm = ParseEm(v, parent.FontSizePx);
                    break;
                case "margin-right":
                    if ((s.WritingMode ?? parent.WritingMode) == WritingMode.Vertical)
                        s.MarginBeforeEm = ParseEm(v, parent.FontSizePx);
                    else
                        s.PadEndEm = Math.Max(s.PadEndEm, Math.Max(0, ParseEm(v, parent.FontSizePx)));
                    break;
                case "margin-bottom":
                case "margin-block-end":
                    if ((s.WritingMode ?? parent.WritingMode) == WritingMode.Vertical)
                        s.PadEndEm = Math.Max(s.PadEndEm, Math.Max(0, ParseEm(v, parent.FontSizePx)));
                    else
                        s.MarginAfterEm = ParseEm(v, parent.FontSizePx);
                    break;
                case "margin":
                {
                    var parts = v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 1) s.MarginBeforeEm = ParseEm(parts[0], parent.FontSizePx);
                    if (parts.Length == 1) s.MarginAfterEm = s.MarginBeforeEm;
                    else if (parts.Length >= 3) s.MarginAfterEm = ParseEm(parts[2], parent.FontSizePx);
                    break;
                }
                case "text-align":
                    s.Align = v.ToLowerInvariant() switch
                    {
                        "center" => TextAlign.Center,
                        "right" or "end" => TextAlign.End,
                        "justify" => TextAlign.Justify,
                        _ => TextAlign.Start
                    };
                    break;
                case "page-break-before":
                case "break-before":
                    s.PageBreakBefore = v is "always" or "page" or "left" or "right";
                    break;
                case "page-break-after":
                case "break-after":
                    s.PageBreakAfter = v is "always" or "page" or "left" or "right";
                    break;
                case "text-combine-upright":
                case "text-combine":
                    s.CombineUpright = !v.Equals("none", StringComparison.OrdinalIgnoreCase);
                    break;
                case "text-emphasis":
                case "text-emphasis-style":
                    s.Emphasis = v.Contains("none", StringComparison.OrdinalIgnoreCase)
                        ? EmphasisKind.None
                        : v.Contains("dot", StringComparison.OrdinalIgnoreCase)
                            ? EmphasisKind.Dot
                            : EmphasisKind.Sesame;
                    break;
                case "color":
                    if (TryParseColor(v, out var c)) s.ColorArgb = c;
                    break;
                case "width":
                    s.WidthEm = ParseEm(v, parent.FontSizePx);
                    break;
                case "height":
                    s.HeightEm = ParseEm(v, parent.FontSizePx);
                    break;
            }
        }
    }

    static float ParseFontSize(string v, float parent)
    {
        v = v.Trim().ToLowerInvariant();
        if (v.EndsWith("em") && float.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var em))
            return parent * em;
        if (v.EndsWith('%') && float.TryParse(v[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
            return parent * (pct / 100f);
        if (v.EndsWith("px") && float.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
            return px;
        if (v.EndsWith("pt") && float.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var pt))
            return pt * 96f / 72f;
        if (v.EndsWith("rem") && float.TryParse(v[..^3], NumberStyles.Float, CultureInfo.InvariantCulture, out var rem))
            return 26f * rem;
        return v switch
        {
            "xx-small" => parent * 0.6f,
            "x-small" => parent * 0.75f,
            "small" or "smaller" => parent * 0.83f,
            "large" or "larger" => parent * 1.2f,
            "x-large" => parent * 1.5f,
            "xx-large" => parent * 1.8f,
            _ => parent
        };
    }

    static bool IsInherit(string v) =>
        v.Equals("inherit", StringComparison.OrdinalIgnoreCase)
        || v.Equals("unset", StringComparison.OrdinalIgnoreCase);

    static float ParseEm(string v, float parentPx)
    {
        v = v.Trim().ToLowerInvariant();
        if (v is "0" or "0px" or "0em" or "normal") return 0;
        if (v.EndsWith("em") && float.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var em))
            return em;
        if (v.EndsWith("px") && float.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
            return parentPx > 0 ? px / parentPx : 0;
        if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var raw))
            return raw;
        return 0;
    }

    static bool TryParseColor(string v, out uint argb)
    {
        argb = 0;
        v = v.Trim();
        if (v.StartsWith('#') && (v.Length == 7 || v.Length == 4))
        {
            if (v.Length == 4)
                v = $"#{v[1]}{v[1]}{v[2]}{v[2]}{v[3]}{v[3]}";
            if (uint.TryParse(v[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                argb = 0xFF000000 | rgb;
                return true;
            }
        }
        return false;
    }

    static string Canonical(string name)
    {
        name = name.ToLowerInvariant();
        if (name.StartsWith("-epub-")) name = name[6..];
        if (name.StartsWith("-webkit-")) name = name[8..];
        if (name.StartsWith("-moz-")) name = name[5..];
        return name;
    }

    sealed class Selector
    {
        public string? Tag;
        public string? Id;
        public string[] Classes = [];
        public WritingMode? Mode;
        public Selector[] Ancestors = [];

        public static Selector? TryParse(string s)
        {
            if (s.Length == 0) return null;
            if (s.Contains(':') || s.Contains('[') || s.Contains('+') || s.Contains('~')) return null;
            var tokens = Regex.Split(s.Trim(), @"\s*>\s*|\s+")
                .Where(t => t.Length > 0)
                .ToArray();
            if (tokens.Length == 0) return null;
            WritingMode? mode = null;
            foreach (var t in tokens)
            {
                if (t is ".vrtl" or "html.vrtl") mode = WritingMode.Vertical;
                else if (t is ".hltr" or "html.hltr") mode = WritingMode.Horizontal;
            }
            var ancestors = new List<Selector>();
            for (int i = 0; i < tokens.Length - 1; i++)
            {
                var anc = ParseSimple(tokens[i], null);
                if (anc == null) return null;
                ancestors.Add(anc);
            }
            var parsed = ParseSimple(tokens[^1], mode);
            if (parsed == null) return null;
            parsed.Ancestors = ancestors.ToArray();
            return parsed;
        }

        static Selector? ParseSimple(string s, WritingMode? mode)
        {
            var sel = new Selector { Mode = mode };
            int i = 0;
            if (s[0] is not '#' and not '.')
            {
                int j = 0;
                while (j < s.Length && s[j] is not '#' and not '.') j++;
                sel.Tag = s[..j];
                if (sel.Tag is "*" or "") sel.Tag = null;
                i = j;
            }
            var classes = new List<string>();
            while (i < s.Length)
            {
                char mark = s[i];
                int j = i + 1;
                while (j < s.Length && s[j] is not '#' and not '.') j++;
                var part = s[(i + 1)..j];
                if (part.Length == 0) return null;
                if (mark == '#') sel.Id = part;
                else if (mark == '.') classes.Add(part);
                else return null;
                i = j;
            }
            sel.Classes = classes.ToArray();
            return sel;
        }

        public bool Matches(
            string tag,
            string? id,
            IEnumerable<string> classes,
            WritingMode? mode,
            IReadOnlyList<(string Tag, string? Id, string[] Classes)> ancestors)
        {
            if (Mode != null)
            {
                var current = mode ?? WritingMode.Vertical;
                if (Mode != current) return false;
            }
            if (!MatchesSimple(tag, id, classes)) return false;
            int ai = 0;
            for (int s = Ancestors.Length - 1; s >= 0; s--)
            {
                var need = Ancestors[s];
                bool found = false;
                while (ai < ancestors.Count)
                {
                    var a = ancestors[ai++];
                    if (need.MatchesSimple(a.Tag, a.Id, a.Classes))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return false;
            }
            return true;
        }

        bool MatchesSimple(string tag, string? id, IEnumerable<string> classes)
        {
            if (Tag != null && !Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)) return false;
            if (Id != null && !Id.Equals(id, StringComparison.Ordinal)) return false;
            foreach (var need in Classes)
            {
                if (!classes.Any(c => c.Equals(need, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }
            return true;
        }
    }
}
