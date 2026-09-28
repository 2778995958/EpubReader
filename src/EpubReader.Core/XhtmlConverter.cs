using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace EpubReader.Core;

public sealed class XhtmlConverter
{
    readonly CssSubset _css;
    readonly Func<string, byte[]?> _load;
    readonly int _threshold;
    readonly string _chapterKey;
    readonly int _spineIndex;
    readonly string _fontFamily;
    readonly List<Illustration> _illustrations;

    public XhtmlConverter(
        CssSubset css,
        Func<string, byte[]?> load,
        int threshold,
        string chapterKey,
        int spineIndex,
        string fontFamily,
        List<Illustration> illustrations)
    {
        _css = css;
        _load = load;
        _threshold = threshold;
        _chapterKey = chapterKey;
        _spineIndex = spineIndex;
        _fontFamily = fontFamily;
        _illustrations = illustrations;
    }

    public List<BlockNode> Convert(string html)
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(html);
        var root = new ComputedStyle { FontFamily = _fontFamily, IndentEm = 0 };
        var blocks = new List<BlockNode>();
        // 從 html 走，才吃得到 html.vrtl / .hltr 的 writing-mode。
        var start = doc.DocumentElement ?? doc.Body;
        if (start == null) return blocks;
        WalkBlocks(start, root, blocks, 0);
        return blocks;
    }

    void WalkBlocks(INode node, ComputedStyle parent, List<BlockNode> blocks, float extraPadTop, string? extraAnchor = null)
    {
        if (node is IElement el)
        {
            var style = StyleOf(el, parent);
            if (style.DisplayNone) return;
            var tag = el.TagName.ToLowerInvariant();
            if (tag is "script" or "style" or "head" or "rp") return;
            if (style.PageBreakBefore) blocks.Add(new PageBreakBlock());
            float padTop = extraPadTop + Math.Max(0, style.PaddingTopEm);
            var anchor = string.IsNullOrEmpty(el.Id) ? extraAnchor : el.Id;

            if (tag is "img" or "image")
            {
                var inlineImgs = new List<InlineNode>();
                if (TryAddInlineImage(el, style, inlineImgs))
                    AddParagraph(blocks, inlineImgs, style, padTop, anchor);
                else
                    TryAddImage(el, blocks, style);
                if (style.PageBreakAfter) blocks.Add(new PageBreakBlock());
                return;
            }

            if (IsBlock(tag))
            {
                int headingLevel = tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
                    ? tag[1] - '0'
                    : style.HeadingLevel;
                if (headingLevel > 0 && !HasBlockChild(el))
                {
                    var inlines = new List<InlineNode>();
                    WalkInlines(el, style, inlines, blocks);
                    if (HasVisibleInline(inlines))
                        blocks.Add(new ParagraphBlock(inlines, 0, style.Align, true, headingLevel, style.MarginBeforeEm, style.MarginAfterEm, padTop, anchor, style.PadEndEm));
                    if (style.PageBreakAfter) blocks.Add(new PageBreakBlock());
                    return;
                }

                if (HasBlockChild(el))
                {
                    foreach (var child in el.ChildNodes)
                        WalkBlocks(child, style, blocks, padTop, anchor);
                    if (style.PageBreakAfter) blocks.Add(new PageBreakBlock());
                    return;
                }

                var pInlines = new List<InlineNode>();
                WalkInlines(el, style, pInlines, blocks);
                AddParagraph(blocks, pInlines, style, padTop, anchor);
                if (style.PageBreakAfter) blocks.Add(new PageBreakBlock());
                return;
            }

            foreach (var child in el.ChildNodes)
                WalkBlocks(child, style, blocks, padTop, anchor);
            if (style.PageBreakAfter) blocks.Add(new PageBreakBlock());
            return;
        }

        if (node.NodeType == NodeType.Text && !string.IsNullOrWhiteSpace(node.TextContent))
        {
            var style = parent.Clone();
            var inlines = new List<InlineNode>();
            AddText(node.TextContent, style, inlines);
            AddParagraph(blocks, inlines, style, extraPadTop, extraAnchor);
        }
    }

    void WalkInlines(INode node, ComputedStyle parent, List<InlineNode> inlines, List<BlockNode> blocks, string? href = null)
    {
        if (node is IElement el)
        {
            var style = StyleOf(el, parent);
            if (style.DisplayNone) return;
            var tag = el.TagName.ToLowerInvariant();
            if (tag is "script" or "style" or "rp") return;
            if (tag is "br")
            {
                inlines.Add(new LineBreakInline());
                return;
            }
            if (tag is "img" or "image")
            {
                if (TryAddInlineImage(el, style, inlines, href))
                    return;
                if (HasVisibleInline(inlines))
                    blocks.Add(new ParagraphBlock(inlines.ToList(), parent.IndentEm, parent.Align, false, 0));
                inlines.Clear();
                TryAddImage(el, blocks, style);
                return;
            }
            if (tag == "a")
            {
                var next = InternalHref(el.GetAttribute("href")) ?? href;
                foreach (var child in el.ChildNodes)
                    WalkInlines(child, style, inlines, blocks, next);
                return;
            }
            if (tag == "ruby")
            {
                AddRuby(el, style, inlines, href);
                return;
            }
            if (style.CombineUpright || tag == "tcy")
            {
                var t = NormalizeTcy(Collapse(el.TextContent));
                if (t.Length > 0)
                    inlines.Add(new TateChuYoko(t, style.ToTextStyle(1), href));
                return;
            }
            foreach (var child in el.ChildNodes)
                WalkInlines(child, style, inlines, blocks, href);
            return;
        }

        if (node.NodeType == NodeType.Text && !string.IsNullOrWhiteSpace(node.TextContent))
            AddText(node.TextContent ?? "", parent, inlines, href);
    }

    void AddRuby(IElement ruby, ComputedStyle style, List<InlineNode> inlines, string? href = null)
    {
        var bases = new List<string>();
        var rts = new List<string>();
        foreach (var child in ruby.ChildNodes)
        {
            if (child is IElement el)
            {
                var t = el.TagName.ToLowerInvariant();
                if (t == "rt") rts.Add(Collapse(el.TextContent));
                else if (t == "rp") continue;
                else if (t == "rb") bases.Add(Collapse(el.TextContent));
                else bases.Add(Collapse(el.TextContent));
            }
            else if (child.NodeType == NodeType.Text)
            {
                var tx = Collapse(child.TextContent ?? "");
                if (tx.Length > 0) bases.Add(tx);
            }
        }
        var b = string.Concat(bases);
        var r = string.Concat(rts);
        if (b.Length == 0) return;
        var ts = style.ToTextStyle(1);
        if (bases.Count == rts.Count && bases.Count > 1)
        {
            for (int i = 0; i < bases.Count; i++)
                inlines.Add(new RubyInline(bases[i], rts[i], ts, href));
        }
        else
            inlines.Add(new RubyInline(b, r, ts, href));
    }

    static void AddParagraph(List<BlockNode> blocks, List<InlineNode> inlines, ComputedStyle style, float padTop, string? anchorId = null)
    {
        if (!HasVisibleInline(inlines)) return;
        blocks.Add(new ParagraphBlock(inlines.ToList(), style.IndentEm, style.Align, false, 0, style.MarginBeforeEm, style.MarginAfterEm, padTop, anchorId, style.PadEndEm));
    }

    static bool HasVisibleInline(IReadOnlyList<InlineNode> inlines)
    {
        foreach (var n in inlines)
        {
            switch (n)
            {
                case TextRun t when !string.IsNullOrWhiteSpace(t.Text):
                case RubyInline:
                case TateChuYoko:
                case InlineImage:
                    return true;
            }
        }
        return false;
    }

    void AddText(string raw, ComputedStyle style, List<InlineNode> inlines, string? href = null)
    {
        var text = NormalizeText(raw);
        if (string.IsNullOrWhiteSpace(text)) return;
        var ts = style.ToTextStyle(1);
        if (style.CombineUpright)
        {
            inlines.Add(new TateChuYoko(NormalizeTcy(Collapse(text)), ts, href));
            return;
        }
        int i = 0;
        while (i < text.Length)
        {
            if (IsTcyDigit(text[i]))
            {
                int j = i;
                while (j < text.Length && j - i < 4 && IsTcyDigit(text[j])) j++;
                int n = j - i;
                if (n is >= 1 and <= 4)
                {
                    if (i > 0) inlines.Add(new TextRun(text[0..i], ts, href));
                    inlines.Add(new TateChuYoko(NormalizeTcy(text[i..j]), ts, href));
                    text = text[j..];
                    i = 0;
                    continue;
                }
            }
            i++;
        }
        if (text.Length > 0)
            inlines.Add(new TextRun(text, ts, href));
    }

    static string? InternalHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href) || Href.IsRemote(href)) return null;
        return href.Trim();
    }

    static bool IsTcyDigit(char c) => c is >= '0' and <= '9' or >= '０' and <= '９';

    static string NormalizeTcy(string t)
    {
        if (t.Length == 0) return t;
        var buf = t.ToCharArray();
        bool changed = false;
        for (int i = 0; i < buf.Length; i++)
        {
            char c = buf[i];
            if (c is >= '０' and <= '９')
            {
                buf[i] = (char)('0' + (c - '０'));
                changed = true;
            }
        }
        return changed ? new string(buf) : t;
    }

    bool TryAddInlineImage(IElement el, ComputedStyle style, List<InlineNode> inlines, string? href = null)
    {
        var classes = (el.GetAttribute("class") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool gaiji = classes.Any(c => c.StartsWith("gaiji", StringComparison.OrdinalIgnoreCase));
        float wEm = style.WidthEm > 0 ? style.WidthEm : (gaiji ? 1f : 0);
        float hEm = style.HeightEm > 0 ? style.HeightEm : (gaiji ? 1f : 0);
        if (!gaiji && (wEm <= 0 || hEm <= 0 || Math.Max(wEm, hEm) > 1.5f))
            return false;
        var src = el.GetAttribute("src")
                  ?? el.GetAttribute("href")
                  ?? el.GetAttribute("xlink:href")
                  ?? el.GetAttribute("xlink:Href");
        if (string.IsNullOrWhiteSpace(src) || Href.IsRemote(src)) return false;
        var key = Href.Combine(_chapterKey, src);
        if (_load(key) == null) return false;
        inlines.Add(new InlineImage(key, Math.Max(0.5f, wEm), Math.Max(0.5f, hEm), style.ToTextStyle(1), href));
        return true;
    }

    void TryAddImage(IElement el, List<BlockNode> blocks, ComputedStyle style)
    {
        var src = el.GetAttribute("src")
                  ?? el.GetAttribute("href")
                  ?? el.GetAttribute("xlink:href")
                  ?? el.GetAttribute("xlink:Href");
        if (string.IsNullOrWhiteSpace(src) || Href.IsRemote(src)) return;
        var key = Href.Combine(_chapterKey, src);
        var bytes = _load(key);
        if (bytes == null) return;
        var size = ImageSizeReader.TryRead(bytes);
        if (size == null) return;
        var block = new IllustrationBlock(
            key, size.Value.Width, size.Value.Height, el.GetAttribute("alt"),
            style.WidthEm, style.HeightEm);
        if (!block.IsOrnament && !block.IsFlowStrip && Math.Max(size.Value.Width, size.Value.Height) < _threshold)
            return;
        blocks.Add(block);
        if (block.IsOrnament || block.IsFlowStrip) return;
        _illustrations.Add(new Illustration
        {
            ResourceKey = key,
            Label = el.GetAttribute("alt") is { Length: > 0 } alt ? alt : $"插圖 {_illustrations.Count + 1}",
            SpineIndex = _spineIndex,
            Width = size.Value.Width,
            Height = size.Value.Height
        });
    }

    ComputedStyle StyleOf(IElement el, ComputedStyle parent)
    {
        var classes = (el.GetAttribute("class") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return _css.Apply(parent, el.TagName.ToLowerInvariant(), el.Id, classes, el.GetAttribute("style"), AncestorsOf(el));
    }

    static List<(string Tag, string? Id, string[] Classes)> AncestorsOf(IElement el)
    {
        var list = new List<(string, string?, string[])>();
        for (var p = el.ParentElement; p != null; p = p.ParentElement)
        {
            var cls = (p.GetAttribute("class") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            list.Add((p.TagName.ToLowerInvariant(), p.Id, cls));
        }
        return list;
    }

    static bool IsBlock(string tag) => tag is
        "p" or "div" or "section" or "article" or "nav" or "header" or "footer"
        or "blockquote" or "li" or "ul" or "ol" or "table" or "tr" or "td" or "th"
        or "figure" or "figcaption" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
        or "body" or "aside" or "main";

    static bool HasBlockChild(IElement el)
    {
        foreach (var c in el.Children)
        {
            if (IsBlock(c.TagName.ToLowerInvariant())) return true;
        }
        return false;
    }

    static string NormalizeText(string s)
    {
        s = s.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\n', ' ');
        s = RegexWs().Replace(s, " ");
        return s;
    }

    static string Collapse(string s) => RegexWs().Replace(s, " ").Trim();

    static System.Text.RegularExpressions.Regex RegexWs() => _ws;
    static readonly System.Text.RegularExpressions.Regex _ws = new(@"[ \t\f\v]+");
}
