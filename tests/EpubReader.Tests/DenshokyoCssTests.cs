using EpubReader.Core;

namespace EpubReader.Tests;

public class DenshokyoCssTests
{
    [Fact]
    public void ClassAliasesWorkWithoutStylesheet()
    {
        var html = """
            <html class="vrtl"><body>
            <p class="indent-1em">一字下げ</p>
            <p class="start-2em">二字下げ</p>
            <p class="h-indent-3em">突き出し</p>
            <p class="end-2em">字上げ</p>
            <p><span class="font-1em30">大</span><span class="font-0em90">小</span><span class="font-050per">半</span></p>
            <p class="gfont bold">ゴシック太字</p>
            <p class="mfont">明朝</p>
            <p class="align-center">中央</p>
            <p class="display-none">隠す</p>
            <p class="pagebreak-before">改ページ</p>
            <p><span class="tcy">12</span><span class="em-sesame">圏</span><span class="color-red">赤</span></p>
            </body></html>
            """;
        var conv = new XhtmlConverter(new CssSubset(), _ => null, 500, "c.xhtml", 0, "Yu Mincho", []);
        var blocks = conv.Convert(html);
        var paras = blocks.OfType<ParagraphBlock>().ToList();

        var indent = paras.First(p => TextOf(p).Contains("一字下げ"));
        Assert.Equal(1f, indent.IndentEm, 2);

        var start = paras.First(p => TextOf(p).Contains("二字下げ"));
        Assert.True(start.PadTopEm >= 1.9f, $"start PadTopEm={start.PadTopEm}");

        var hang = paras.First(p => TextOf(p).Contains("突き出し"));
        Assert.True(hang.IndentEm <= -2.9f, $"hang IndentEm={hang.IndentEm}");
        Assert.True(hang.PadTopEm >= 2.9f, $"hang PadTopEm={hang.PadTopEm}");

        var end = paras.First(p => TextOf(p).Contains("字上げ"));
        Assert.True(end.PadEndEm >= 1.9f, $"PadEndEm={end.PadEndEm}");

        var sizes = paras.First(p => TextOf(p).Contains("大"));
        var big = sizes.Inlines.OfType<TextRun>().First(t => t.Text.Contains("大"));
        var small = sizes.Inlines.OfType<TextRun>().First(t => t.Text.Contains("小"));
        var half = sizes.Inlines.OfType<TextRun>().First(t => t.Text.Contains("半"));
        Assert.InRange(big.Style.FontSizePx, 26 * 1.25f, 26 * 1.35f);
        Assert.InRange(small.Style.FontSizePx, 26 * 0.85f, 26 * 0.95f);
        Assert.InRange(half.Style.FontSizePx, 26 * 0.45f, 26 * 0.55f);

        var gothic = paras.First(p => TextOf(p).Contains("ゴシック"));
        var g = gothic.Inlines.OfType<TextRun>().First();
        Assert.Contains("Gothic", g.Style.FontFamily, StringComparison.OrdinalIgnoreCase);
        Assert.True(g.Style.Bold);

        var mincho = paras.First(p => TextOf(p).Contains("明朝"));
        Assert.Contains("Mincho", mincho.Inlines.OfType<TextRun>().First().Style.FontFamily, StringComparison.OrdinalIgnoreCase);

        var center = paras.First(p => TextOf(p).Contains("中央"));
        Assert.Equal(TextAlign.Center, center.Align);

        Assert.DoesNotContain(paras, p => TextOf(p).Contains("隠す"));
        Assert.Contains(blocks, b => b is PageBreakBlock);

        var marks = paras.First(p => p.Inlines.OfType<TateChuYoko>().Any());
        Assert.Contains(marks.Inlines.OfType<TateChuYoko>(), t => t.Text == "12");
        var sesame = marks.Inlines.OfType<TextRun>().First(t => t.Text.Contains("圏"));
        Assert.Equal(EmphasisKind.Sesame, sesame.Style.Emphasis);
        var red = marks.Inlines.OfType<TextRun>().First(t => t.Text.Contains("赤"));
        Assert.Equal(0xFFFF0000u, red.Style.ColorArgb);
    }

    [Fact]
    public void GfontIsGothicNotBold()
    {
        var s = new CssSubset().Apply(new ComputedStyle(), "span", null, ["gfont"], null);
        Assert.Contains("Gothic", s.FontFamily, StringComparison.OrdinalIgnoreCase);
        Assert.False(s.Bold);
    }

    [Fact]
    public void InheritTextIndentFromParent()
    {
        var css = CssSubset.Parse("div { text-indent: 2em; } p { text-indent: inherit; }");
        var html = """<html class="vrtl"><body><div><p>継承</p></div></body></html>""";
        var conv = new XhtmlConverter(css, _ => null, 500, "c.xhtml", 0, "Yu Mincho", []);
        var para = Assert.Single(conv.Convert(html).OfType<ParagraphBlock>());
        Assert.Equal(2f, para.IndentEm, 2);
    }

    [Fact]
    public void EndClassShortensVerticalLine()
    {
        var html = """
            <html class="vrtl"><body>
            <p class="end-5em">一二三四五六七八九十</p>
            </body></html>
            """;
        var conv = new XhtmlConverter(new CssSubset(), _ => null, 500, "c.xhtml", 0, "Yu Mincho", []);
        var para = Assert.Single(conv.Convert(html).OfType<ParagraphBlock>());
        Assert.True(para.PadEndEm >= 4.9f);

        using var m = new DummyMeasurer();
        var pages = LayoutEngine.Layout(new Book
        {
            FilePath = "x",
            Title = "t",
            Spine =
            [
                new SpineItem
                {
                    Index = 0, Key = "c", Title = "c", Kind = SpineKind.Reflowable,
                    Blocks = [para]
                }
            ]
        }, new LayoutSettings
        {
            WritingMode = WritingMode.Vertical,
            PageWidth = 400,
            PageHeight = 400,
            Margin = 40
        }, m);
        var texts = pages.SelectMany(p => p.Ops).OfType<DrawTextOp>().ToList();
        Assert.NotEmpty(texts);
        float pageBottom = 400 - 40;
        Assert.All(texts, t => Assert.True(t.Y + t.Height <= pageBottom - 26 * 4, $"Y={t.Y} h={t.Height}"));
    }

    [Fact]
    public void HangingIndentContinuationStaysPadded()
    {
        var html = """
            <html class="vrtl"><body>
            <p class="h-indent-2em">あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほ</p>
            </body></html>
            """;
        var conv = new XhtmlConverter(new CssSubset(), _ => null, 500, "c.xhtml", 0, "Yu Mincho", []);
        var para = Assert.Single(conv.Convert(html).OfType<ParagraphBlock>());
        using var m = new DummyMeasurer();
        var pages = LayoutEngine.Layout(new Book
        {
            FilePath = "x",
            Title = "t",
            Spine =
            [
                new SpineItem
                {
                    Index = 0, Key = "c", Title = "c", Kind = SpineKind.Reflowable,
                    Blocks = [para]
                }
            ]
        }, new LayoutSettings
        {
            WritingMode = WritingMode.Vertical,
            PageWidth = 600,
            PageHeight = 280,
            Margin = 40,
            LineHeight = 1.8f
        }, m);
        var cols = pages.SelectMany(p => p.Ops).OfType<DrawTextOp>()
            .GroupBy(t => MathF.Round(t.X, 0))
            .OrderByDescending(g => g.Key)
            .ToList();
        Assert.True(cols.Count >= 2, $"cols={cols.Count}");
        float firstY = cols[0].Min(t => t.Y);
        float nextY = cols[1].Min(t => t.Y);
        Assert.True(firstY < 40 + 10, $"firstY={firstY}");
        Assert.True(nextY >= 40 + 26 * 1.5f, $"nextY={nextY} should keep hanging pad");
    }

    [Fact]
    public void ParsesOfficialStyleStandard()
    {
        var path = Path.Combine(
            "D:", "git", "AeroEpubViewer",
            "dpfj_epub3guide_ver1.1.4", "付録",
            "book-template_2025-09-11", "item", "style", "style-standard.css");
        if (!File.Exists(path)) return;
        var css = CssSubset.Parse(File.ReadAllText(path));
        var vrtl = new[] { ("html", (string?)null, new[] { "vrtl" }) };
        var indent = css.Apply(new ComputedStyle { WritingMode = WritingMode.Vertical }, "p", null, ["indent-2em"], null, vrtl);
        Assert.Equal(2f, indent.IndentEm, 2);
        var start = css.Apply(new ComputedStyle { WritingMode = WritingMode.Vertical }, "p", null, ["start-2em"], null, vrtl);
        Assert.True(start.PaddingTopEm >= 1.9f, $"start PaddingTopEm={start.PaddingTopEm}");
        var font = css.Apply(new ComputedStyle { WritingMode = WritingMode.Vertical, FontSizePx = 26 }, "span", null, ["font-1em30"], null, vrtl);
        Assert.InRange(font.FontSizePx, 26 * 1.25f, 26 * 1.35f);
        var tcy = css.Apply(new ComputedStyle { WritingMode = WritingMode.Vertical }, "span", null, ["tcy"], null, vrtl);
        Assert.True(tcy.CombineUpright);
        var hang = css.Apply(new ComputedStyle { WritingMode = WritingMode.Vertical }, "p", null, ["h-indent-3em"], null, vrtl);
        Assert.True(hang.IndentEm <= -2.9f);
        Assert.True(hang.PaddingTopEm >= 2.9f);
    }

    [Fact]
    public void DefaultBodyIndentIsZeroLikeDenshokyoReset()
    {
        var html = "<html class=\"vrtl\"><body><p>地の文</p></body></html>";
        var conv = new XhtmlConverter(new CssSubset(), _ => null, 500, "c.xhtml", 0, "Yu Mincho", []);
        var para = Assert.Single(conv.Convert(html).OfType<ParagraphBlock>());
        Assert.True(para.IndentEm < 0.01f, $"IndentEm={para.IndentEm}");
    }

    [Fact]
    public void LetterSpacingClassWidensAdvance()
    {
        var html = """
            <html class="vrtl"><body>
            <p>あ</p>
            <p class="lspacing-1em">あ</p>
            </body></html>
            """;
        var conv = new XhtmlConverter(new CssSubset(), _ => null, 500, "c.xhtml", 0, "Yu Mincho", []);
        var paras = conv.Convert(html).OfType<ParagraphBlock>().ToList();
        Assert.Equal(2, paras.Count);
        Assert.Equal(1f, paras[1].Inlines.OfType<TextRun>().First().Style.LetterSpacingEm, 2);

        using var m = new DummyMeasurer();
        List<PageModel> Pages(ParagraphBlock p) => LayoutEngine.Layout(new Book
        {
            FilePath = "x",
            Title = "t",
            Spine =
            [
                new SpineItem
                {
                    Index = 0, Key = "c", Title = "c", Kind = SpineKind.Reflowable,
                    Blocks = [p]
                }
            ]
        }, new LayoutSettings
        {
            WritingMode = WritingMode.Vertical,
            PageWidth = 400,
            PageHeight = 400,
            Margin = 40
        }, m);

        // 單字無法看出字間；用兩字比較行長。
        var plain = new XhtmlConverter(new CssSubset(), _ => null, 500, "c.xhtml", 0, "Yu Mincho", [])
            .Convert("<html class=\"vrtl\"><body><p>あい</p></body></html>")
            .OfType<ParagraphBlock>().Single();
        var spaced = new XhtmlConverter(new CssSubset(), _ => null, 500, "c.xhtml", 0, "Yu Mincho", [])
            .Convert("<html class=\"vrtl\"><body><p class=\"lspacing-1em\">あい</p></body></html>")
            .OfType<ParagraphBlock>().Single();
        float Span(IEnumerable<DrawTextOp> ops) => ops.Max(o => o.Y + o.Height) - ops.Min(o => o.Y);
        float a = Span(Pages(plain)[0].Ops.OfType<DrawTextOp>());
        float b = Span(Pages(spaced)[0].Ops.OfType<DrawTextOp>());
        Assert.True(b > a + 10, $"plain={a} spaced={b}");
    }

    static string TextOf(ParagraphBlock p) =>
        string.Concat(p.Inlines.Select(n => n switch
        {
            TextRun t => t.Text,
            TateChuYoko t => t.Text,
            RubyInline r => r.BaseText,
            _ => ""
        }));
}
