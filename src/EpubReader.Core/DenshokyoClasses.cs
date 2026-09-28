using System.Globalization;
using System.Text.RegularExpressions;

namespace EpubReader.Core;

/// <summary>
/// 電書協 EPUB 3 制作指南 style-standard / style-advance 的 class 別名。
/// 書內 CSS 缺漏或選擇器解析不到時，仍能套用常用排版。
/// </summary>
static class DenshokyoClasses
{
    static readonly string[] SizePrefixes =
    [
        "max-measure-", "max-extent-", "max-height-", "max-width-", "max-size-",
        "line-height-", "lspacing-", "h-indent-",
        "m-before-", "m-after-", "m-start-", "m-end-",
        "m-top-", "m-left-", "m-right-", "m-bottom-",
        "p-before-", "p-after-", "p-start-", "p-end-",
        "p-top-", "p-left-", "p-right-", "p-bottom-",
        "measure-", "extent-", "indent-", "start-", "font-",
        "width-", "height-", "end-",
        "m-", "p-"
    ];

    static readonly Dictionary<string, uint> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = 0xFF000000,
        ["dimgray"] = 0xFF696969,
        ["gray"] = 0xFF808080,
        ["darkgray"] = 0xFFA9A9A9,
        ["silver"] = 0xFFC0C0C0,
        ["gainsboro"] = 0xFFDCDCDC,
        ["red"] = 0xFFFF0000,
        ["blue"] = 0xFF0000FF,
        ["cyan"] = 0xFF00FFFF,
        ["magenta"] = 0xFFFF00FF,
        ["orangered"] = 0xFFFF4500
    };

    public static bool Apply(ComputedStyle s, string cls)
    {
        cls = cls.ToLowerInvariant();
        if (ApplyExact(s, cls)) return true;
        return ApplySized(s, cls);
    }

    public static string MapFamily(string family)
    {
        var x = family.Trim().Trim('"', '\'').ToLowerInvariant();
        if (x is "sans-serif-ja" or "sans-serif" or "sans-serif-ja-v")
            return "Yu Gothic";
        if (x is "serif-ja" or "serif-ja-v" or "serif")
            return "Yu Mincho";
        return family.Trim().Trim('"', '\'');
    }

    public static bool IsGothic(string family) =>
        family.Contains("Gothic", StringComparison.OrdinalIgnoreCase)
        || family.Contains("ゴシック", StringComparison.Ordinal)
        || family.Contains("Hei", StringComparison.OrdinalIgnoreCase)
        || family.Contains("黑體", StringComparison.Ordinal)
        || family.Contains("黑体", StringComparison.Ordinal)
        || family.Contains("sans", StringComparison.OrdinalIgnoreCase);

    static bool ApplyExact(ComputedStyle s, string cls)
    {
        switch (cls)
        {
            case "tcy" or "text-combine" or "maxion-tcy" or "tate-chu-yoko"
                or "upright" or "upright-1":
                s.CombineUpright = true;
                return true;
            case "em-sesame" or "em-sesame-open" or "sesame" or "keigakuten" or "emphasis":
                s.Emphasis = EmphasisKind.Sesame;
                return true;
            case "em-dot" or "em-dots" or "em-dot-open"
                or "em-circle" or "em-circle-open"
                or "em-double-circle" or "em-double-circle-open"
                or "em-triangle" or "em-triangle-open":
                s.Emphasis = EmphasisKind.Dot;
                return true;
            case "bold":
                s.Bold = true;
                return true;
            case "font-weight-normal":
                s.Bold = false;
                return true;
            case "italic":
                s.Italic = true;
                return true;
            case "font-style-normal":
                s.Italic = false;
                return true;
            case "gfont":
                s.FontFamily = "Yu Gothic";
                return true;
            case "mfont":
                s.FontFamily = "Yu Mincho";
                return true;
            case "gaiji" or "gaiji-line" or "gaiji-wide":
                if (s.WidthEm <= 0) s.WidthEm = 1;
                if (s.HeightEm <= 0) s.HeightEm = 1;
                return true;
            case "center" or "align-center" or "block-align-center":
                s.Align = TextAlign.Center;
                return true;
            case "align-left" or "align-start":
                s.Align = TextAlign.Start;
                return true;
            case "align-right" or "align-end":
                s.Align = TextAlign.End;
                return true;
            case "align-justify":
                s.Align = TextAlign.Justify;
                return true;
            case "display-none":
                s.DisplayNone = true;
                return true;
            case "pagebreak-before":
                s.PageBreakBefore = true;
                return true;
            case "pagebreak":
                s.PageBreakAfter = true;
                return true;
            case "pagebreak-both":
                s.PageBreakBefore = true;
                s.PageBreakAfter = true;
                return true;
            case "kogaki":
                s.FontSizePx *= 0.75f;
                return true;
            case "super" or "sub" or "key" or "ref":
                s.FontSizePx *= 0.8f;
                return true;
            case "kunten" or "kunten-okuri":
                s.FontSizePx *= 0.67f;
                return true;
            case "oo-midashi" or "oomidashi" or "o-midashi"
                or "chapter-title" or "chaptertitle" or "chap-title"
                or "book-title" or "main-title" or "title" or "tobira-midashi":
                if (s.HeadingLevel == 0)
                    CssSubset.ApplyHeadingUa(s, s.FontSizePx, 1);
                return true;
            case "naka-midashi" or "nakamidashi" or "midashi" or "section-title":
                if (s.HeadingLevel == 0)
                    CssSubset.ApplyHeadingUa(s, s.FontSizePx, 2);
                return true;
            case "ko-midashi" or "komidashi":
                if (s.HeadingLevel == 0)
                    CssSubset.ApplyHeadingUa(s, s.FontSizePx, 3);
                return true;
        }

        if (cls.StartsWith("color-", StringComparison.Ordinal) && Colors.TryGetValue(cls[6..], out var color))
        {
            s.ColorArgb = color;
            return true;
        }
        return false;
    }

    static bool ApplySized(ComputedStyle s, string cls)
    {
        foreach (var prefix in SizePrefixes)
        {
            if (!cls.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rest = cls[prefix.Length..];
            if (!TryParseSizeToken(rest, out var em, out var percent))
                return false;
            ApplySizedPrefix(s, prefix, em, percent);
            return true;
        }
        return false;
    }

    static void ApplySizedPrefix(ComputedStyle s, string prefix, float em, bool percent)
    {
        switch (prefix)
        {
            case "font-":
                s.FontSizePx *= em;
                break;
            case "indent-":
                s.IndentEm = em;
                break;
            case "h-indent-":
                s.IndentEm = -em;
                s.PaddingTopEm = em;
                break;
            case "start-":
                if (IsVertical(s))
                    s.PaddingTopEm = em;
                else
                    s.IndentEm = em;
                break;
            case "end-":
                s.PadEndEm = em;
                break;
            case "lspacing-":
                s.LetterSpacingEm = em;
                break;
            case "width-" or "measure-":
                if (!percent) s.WidthEm = em;
                break;
            case "height-" or "extent-":
                if (!percent) s.HeightEm = em;
                break;
            case "max-width-" or "max-size-" or "max-measure-":
                if (!percent && em > 0)
                    s.WidthEm = s.WidthEm > 0 ? Math.Min(s.WidthEm, em) : em;
                break;
            case "max-height-" or "max-extent-":
                if (!percent && em > 0)
                    s.HeightEm = s.HeightEm > 0 ? Math.Min(s.HeightEm, em) : em;
                break;
            case "line-height-":
                break;
            default:
                if (percent) break;
                ApplyBoxClass(s, prefix, em);
                break;
        }
    }

    static void ApplyBoxClass(ComputedStyle s, string prefix, float em)
    {
        var name = prefix.TrimEnd('-');
        if (name is "m" or "p")
        {
            AddEdge(s, BoxEdge.Start, em);
            AddEdge(s, BoxEdge.End, em);
            AddEdge(s, BoxEdge.Before, em);
            AddEdge(s, BoxEdge.After, em);
            return;
        }
        var side = name.StartsWith("m-", StringComparison.Ordinal) ? name[2..]
            : name.StartsWith("p-", StringComparison.Ordinal) ? name[2..]
            : "";
        if (side.Length == 0) return;
        AddEdge(s, Physical(s, side), em);
    }

    enum BoxEdge { Start, End, Before, After }

    static void AddEdge(ComputedStyle s, BoxEdge edge, float em)
    {
        switch (edge)
        {
            case BoxEdge.Start:
                s.PaddingTopEm = Math.Max(s.PaddingTopEm, em);
                break;
            case BoxEdge.End:
                s.PadEndEm = Math.Max(s.PadEndEm, em);
                break;
            case BoxEdge.Before:
                s.MarginBeforeEm = em == 0 ? 0 : Math.Max(s.MarginBeforeEm, em);
                break;
            case BoxEdge.After:
                s.MarginAfterEm = em == 0 ? 0 : Math.Max(s.MarginAfterEm, em);
                break;
        }
        if (em == 0 && edge == BoxEdge.Start) s.PaddingTopEm = 0;
        if (em == 0 && edge == BoxEdge.End) s.PadEndEm = 0;
    }

    static BoxEdge Physical(ComputedStyle s, string side)
    {
        bool v = IsVertical(s);
        return (side, v) switch
        {
            ("top", true) or ("start", _) => BoxEdge.Start,
            ("top", false) or ("before", _) => BoxEdge.Before,
            ("bottom", true) or ("end", _) => BoxEdge.End,
            ("bottom", false) or ("after", _) => BoxEdge.After,
            ("left", true) => BoxEdge.After,
            ("left", false) => BoxEdge.Start,
            ("right", true) => BoxEdge.Before,
            ("right", false) => BoxEdge.End,
            _ => BoxEdge.Before
        };
    }

    static bool IsVertical(ComputedStyle s) =>
        (s.WritingMode ?? WritingMode.Vertical) == WritingMode.Vertical;

    static bool TryParseSizeToken(string rest, out float em, out bool percent)
    {
        em = 0;
        percent = false;
        if (rest is "0" or "0em" or "000per") return true;
        if (rest is "auto" or "none" or "normal") return false;
        if (rest.EndsWith("per", StringComparison.Ordinal))
        {
            if (int.TryParse(rest[..^3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
            {
                em = p / 100f;
                percent = true;
                return true;
            }
            return false;
        }
        var m = Regex.Match(rest, @"^(\d+)em(\d{2})?$");
        if (!m.Success) return false;
        em = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        if (m.Groups[2].Success)
            em += int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 100f;
        return true;
    }
}
