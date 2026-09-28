namespace EpubReader.Core;

public enum WritingMode
{
    Horizontal,
    Vertical
}

public enum PageProgression
{
    Ltr,
    Rtl
}

public enum EmphasisKind
{
    None,
    Sesame,
    Dot
}

public enum TextAlign
{
    Start,
    Center,
    End,
    Justify
}

public sealed class TextStyle
{
    public float FontSizePx { get; init; } = 26;
    public string FontFamily { get; init; } = "Yu Mincho";
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public EmphasisKind Emphasis { get; init; }
    public uint ColorArgb { get; init; } = 0xFF2A2118;
    public float LetterSpacingEm { get; init; }

    public TextStyle WithSize(float size) => new()
    {
        FontSizePx = size,
        FontFamily = FontFamily,
        Bold = Bold,
        Italic = Italic,
        Emphasis = Emphasis,
        ColorArgb = ColorArgb,
        LetterSpacingEm = LetterSpacingEm
    };
}

public abstract record InlineNode;

public sealed record TextRun(string Text, TextStyle Style, string? Href = null) : InlineNode;

public sealed record RubyInline(string BaseText, string Annotation, TextStyle Style, string? Href = null) : InlineNode;

public sealed record TateChuYoko(string Text, TextStyle Style, string? Href = null) : InlineNode;

public sealed record InlineImage(string ResourceKey, float WidthEm, float HeightEm, TextStyle Style, string? Href = null) : InlineNode;

public sealed record LineBreakInline : InlineNode;

public abstract record BlockNode;

public sealed record ParagraphBlock(
    IReadOnlyList<InlineNode> Inlines,
    float IndentEm,
    TextAlign Align,
    bool IsHeading,
    int HeadingLevel,
    float SpaceBeforeEm = 0,
    float SpaceAfterEm = 0,
    float PadTopEm = 0,
    string? AnchorId = null,
    float PadEndEm = 0) : BlockNode;

public sealed record IllustrationBlock(
    string ResourceKey,
    int PixelWidth,
    int PixelHeight,
    string? Alt,
    float WidthEm = 0,
    float HeightEm = 0) : BlockNode
{
    public bool IsOrnament =>
        WidthEm > 0 && HeightEm > 0 && Math.Max(WidthEm, HeightEm) <= 12f;

    public bool IsFlowStrip =>
        PixelWidth > 0 && PixelHeight > 0 &&
        (PixelWidth / (float)PixelHeight < 0.22f || PixelHeight / (float)PixelWidth < 0.22f);
}

public sealed record PageBreakBlock : BlockNode;

public enum SpineKind
{
    Reflowable,
    Image
}

public sealed class SpineItem
{
    public required int Index { get; init; }
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required SpineKind Kind { get; init; }
    public PageSpreadKind Spread { get; init; }
    public IReadOnlyList<BlockNode> Blocks { get; init; } = [];
    public string? ImageKey { get; init; }
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
}

public sealed class TocItem
{
    public required string Title { get; init; }
    public int SpineIndex { get; init; }
    public IReadOnlyList<TocItem> Children { get; init; } = [];
}

public sealed class Illustration
{
    public required string ResourceKey { get; init; }
    public required string Label { get; init; }
    public int SpineIndex { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

public sealed class Book : IDisposable
{
    public required string FilePath { get; init; }
    public required string Title { get; init; }
    public string Author { get; init; } = "";
    public string Language { get; init; } = "ja";
    public WritingMode DefaultWritingMode { get; init; } = WritingMode.Vertical;
    public PageProgression Progression { get; init; } = PageProgression.Rtl;
    public bool IsManga { get; init; }
    public bool IsFixedLayout { get; init; }
    public bool HasPageSpreads { get; init; }
    /// <summary>true＝橫排左右（left+right）；false＝直排右左（right+left）。</summary>
    public bool PairLeftToRight { get; init; }
    public IReadOnlyList<SpineItem> Spine { get; init; } = [];
    public IReadOnlyList<TocItem> Toc { get; init; } = [];
    public IReadOnlyList<Illustration> Illustrations { get; init; } = [];
    public Func<string, byte[]?>? LoadResource { get; init; }
    public IDisposable? Lifetime { get; init; }
    public int ImageThreshold { get; init; } = 500;

    public void Dispose()
    {
        Lifetime?.Dispose();
        GC.SuppressFinalize(this);
    }
}

public enum PageSpreadKind
{
    None,
    Left,
    Right,
    Center
}

public readonly record struct MeasuredSize(float Width, float Height, float Advance);

public interface ITextMeasurer : IDisposable
{
    MeasuredSize Measure(string text, TextStyle style, WritingMode mode);
}

public abstract record DrawOp;

public sealed record DrawTextOp(
    string Text,
    float X,
    float Y,
    float Width,
    float Height,
    TextStyle Style,
    WritingMode Mode,
    bool UprightCombine,
    string? Href = null) : DrawOp;

public sealed record DrawRubyOp(
    string BaseText,
    string Annotation,
    float X,
    float Y,
    float Width,
    float Height,
    TextStyle Style,
    WritingMode Mode,
    string? Href = null) : DrawOp;

public sealed record DrawImageOp(
    string ResourceKey,
    float X,
    float Y,
    float Width,
    float Height) : DrawOp;

public sealed class PageModel
{
    public int Index { get; set; }
    public int SpineIndex { get; set; }
    public int CharOffset { get; set; }
    public int EndOffset { get; set; }
    public bool IsImagePage { get; set; }
    public PageSpreadKind Spread { get; set; }
    public List<DrawOp> Ops { get; } = [];
    public List<(string Id, int CharOffset)> Anchors { get; } = [];
}

public sealed class LayoutSettings
{
    public WritingMode WritingMode { get; set; } = WritingMode.Vertical;
    public float FontScale { get; set; } = 1f;
    public float PageWidth { get; set; } = 800;
    public float PageHeight { get; set; } = 1000;
    public float Margin { get; set; } = 44;
    public float LineHeight { get; set; } = 1.8f;
    public bool Spread { get; set; }
    /// <summary>橫排或 spine 左右：畫面左至右（含雙頁圖）。</summary>
    public bool VisualLtr { get; set; }
    public string FontFamily { get; set; } = "Yu Mincho";
    public uint PaperArgb { get; set; } = 0xFFF3EAD3;
    public uint InkArgb { get; set; } = 0xFF2A2118;
}

public sealed class BookProgress
{
    public int SpineIndex { get; set; }
    public int CharOffset { get; set; }
    public WritingMode? WritingMode { get; set; }
    public float FontScale { get; set; } = 1f;
    public bool Spread { get; set; }
}

public sealed class RecentEntry
{
    public required string Path { get; set; }
    public required string Title { get; set; }
    public DateTime OpenedUtc { get; set; }
}

public static class Href
{
    public static string Combine(string baseKey, string relative)
    {
        relative = relative.Trim();
        if (relative.Length == 0) return baseKey;
        if (relative.Contains("://", StringComparison.Ordinal) || relative.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return relative;
        relative = relative.Split('#', 2)[0].Replace('\\', '/');
        if (relative.StartsWith('/'))
            return relative.TrimStart('/');
        var slash = baseKey.Replace('\\', '/').LastIndexOf('/');
        var dir = slash < 0 ? "" : baseKey[..slash];
        var parts = new List<string>();
        if (dir.Length > 0)
            parts.AddRange(dir.Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (var part in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(part);
        }
        return string.Join('/', parts);
    }

    public static bool IsRemote(string href) =>
        href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || href.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
        || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase);

    public static (string File, string Frag) SplitHash(string href)
    {
        href = href.Trim();
        int i = href.IndexOf('#');
        return i < 0 ? (href, "") : (href[..i], href[(i + 1)..]);
    }
}
