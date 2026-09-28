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
    public TextAlign Align { get; set; } = TextAlign.Start;
    public bool DisplayNone { get; set; }
    public bool PageBreakBefore { get; set; }
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
        ColorArgb = ColorArgb
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

    public ComputedStyle Apply(ComputedStyle parent, string tag, string? id, IEnumerable<string> classes, string? inlineStyle)
    {
        var s = parent.Clone();
        s.PageBreakBefore = false;
        s.CombineUpright = false;
        s.HeadingLevel = 0;
        s.PaddingTopEm = 0; // padding 不繼承，由 DOM 走訪累加
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
            ApplyClassAlias(s, cls);

        var classList = classes as IList<string> ?? classes.ToList();
        foreach (var (sel, props) in _rules)
        {
            if (sel.Matches(tag, id, classList, s.WritingMode ?? parent.WritingMode))
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

    static void ApplyHeadingUa(ComputedStyle s, float parentPx, int level)
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

    static void ApplyClassAlias(ComputedStyle s, string cls)
    {
        cls = cls.ToLowerInvariant();
        if (cls is "tcy" or "text-combine" or "maxion-tcy" or "tate-chu-yoko" or "upright")
            s.CombineUpright = true;
        if (cls is "em-sesame" or "sesame" or "em" or "keigakuten" or "emphasis" or "em-dot" or "em-dots")
            s.Emphasis = cls.Contains("dot") ? EmphasisKind.Dot : EmphasisKind.Sesame;
        if (cls is "bold" or "gfont")
            s.Bold = true;
        if (cls is "center")
            s.Align = TextAlign.Center;
        if (TryParseEmClass(cls, "font-", out var fontEm) && fontEm > 0)
            s.FontSizePx *= fontEm;
        if (TryParseEmClass(cls, "start-", out var startEm))
        {
            if (s.WritingMode == WritingMode.Horizontal)
                s.IndentEm = startEm;
            else
                s.PaddingTopEm = Math.Max(s.PaddingTopEm, startEm);
        }
        if (s.HeadingLevel == 0)
        {
            if (cls is "oo-midashi" or "oomidashi" or "o-midashi" or "chapter-title" or "chaptertitle"
                or "chap-title" or "book-title" or "main-title" or "title")
                ApplyHeadingUa(s, s.FontSizePx, 1);
            else if (cls is "naka-midashi" or "nakamidashi" or "midashi" or "section-title")
                ApplyHeadingUa(s, s.FontSizePx, 2);
            else if (cls is "ko-midashi" or "komidashi")
                ApplyHeadingUa(s, s.FontSizePx, 3);
        }
    }

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
                    s.FontFamily = v.Split(',')[0].Trim().Trim('"', '\'');
                    break;
                case "font-weight":
                    s.Bold = v is "bold" or "700" or "800" or "900" or "bolder";
                    break;
                case "font-style":
                    s.Italic = v.Equals("italic", StringComparison.OrdinalIgnoreCase) || v.Equals("oblique", StringComparison.OrdinalIgnoreCase);
                    break;
                case "text-indent":
                    s.IndentEm = ParseEm(v, parent.FontSizePx);
                    break;
                case "padding-top":
                    s.PaddingTopEm = Math.Max(0, ParseEm(v, parent.FontSizePx));
                    break;
                case "padding":
                {
                    var pad = v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (pad.Length >= 1)
                        s.PaddingTopEm = Math.Max(0, ParseEm(pad[0], parent.FontSizePx));
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
                    break;
                case "margin-bottom":
                case "margin-block-end":
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
            "small" => parent * 0.9f,
            "large" => parent * 1.2f,
            "x-large" => parent * 1.5f,
            "xx-large" => parent * 1.8f,
            _ => parent
        };
    }

    static float ParseEm(string v, float parentPx)
    {
        v = v.Trim().ToLowerInvariant();
        if (v.EndsWith("em") && float.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var em))
            return em;
        if (v.EndsWith("px") && float.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
            return parentPx > 0 ? px / parentPx : 0;
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

    static bool TryParseEmClass(string cls, string prefix, out float em)
    {
        em = 0;
        if (!cls.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var rest = cls[prefix.Length..];
        if (rest is "0") return true;
        var m = Regex.Match(rest, @"^(\d+)em(\d{2})?$");
        if (!m.Success) return false;
        em = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        if (m.Groups[2].Success)
            em += int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 100f;
        return true;
    }

    sealed class Selector
    {
        public string? Tag;
        public string? Id;
        public string[] Classes = [];
        public WritingMode? Mode;

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
            return ParseSimple(tokens[^1], mode);
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

        public bool Matches(string tag, string? id, IEnumerable<string> classes, WritingMode? mode)
        {
            if (Mode != null)
            {
                var current = mode ?? WritingMode.Vertical;
                if (Mode != current) return false;
            }
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
