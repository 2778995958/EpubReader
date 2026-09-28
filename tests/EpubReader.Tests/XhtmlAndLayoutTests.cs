using EpubReader.Core;

namespace EpubReader.Tests;

public class XhtmlAndLayoutTests
{
    [Fact]
    public void DropsSmallImagesKeepsLarge()
    {
        var small = PngWriter.Make(32, 32, 0, 0, 0);
        var large = BmpWriter.Make(600, 800, 200, 10, 10);
        var html = """
            <html><body>
            <p>本文<ruby>漢字<rt>かんじ</rt></ruby>12。</p>
            <p><img src="small.png"/><img src="big.bmp"/></p>
            </body></html>
            """;
        byte[]? Load(string key) => key.EndsWith("small.png") ? small : key.EndsWith("big.bmp") ? large : null;
        var ill = new List<Illustration>();
        var conv = new XhtmlConverter(new CssSubset(), Load, 500, "chap.xhtml", 0, "Yu Mincho", ill);
        var blocks = conv.Convert(html);
        Assert.DoesNotContain(blocks.OfType<IllustrationBlock>(), b => b.ResourceKey.Contains("small"));
        Assert.Contains(blocks.OfType<IllustrationBlock>(), b => b.ResourceKey.Contains("big"));
        Assert.Single(ill);
        Assert.Contains(blocks.OfType<ParagraphBlock>(), p => p.Inlines.OfType<RubyInline>().Any());
        Assert.Contains(blocks.OfType<ParagraphBlock>(), p => p.Inlines.OfType<TateChuYoko>().Any());
    }

    [Fact]
    public void GaijiStaysInlineInSentence()
    {
        var glyph = PngWriter.Make(128, 128, 10, 10, 10);
        var css = CssSubset.Parse("img.gaiji { width: 1em; height: 1em; }");
        var html = """
            <html class="vrtl"><body>
            <p>神絵<img class="gaiji" src="gaiji-0001.png" alt=""/><img class="gaiji" src="gaiji-0001.png" alt=""/>っ</p>
            </body></html>
            """;
        var conv = new XhtmlConverter(css, key => key.Contains("gaiji") ? glyph : null, 500, "xhtml/p-006.xhtml", 0, "Yu Mincho", []);
        var blocks = conv.Convert(html);
        Assert.DoesNotContain(blocks, b => b is IllustrationBlock);
        var para = Assert.Single(blocks.OfType<ParagraphBlock>());
        Assert.Equal(2, para.Inlines.OfType<InlineImage>().Count());
        Assert.Contains(para.Inlines.OfType<TextRun>(), t => t.Text.Contains("神絵"));
        Assert.Contains(para.Inlines.OfType<TextRun>(), t => t.Text.Contains("っ"));

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
                    Blocks = blocks
                }
            ]
        }, new LayoutSettings
        {
            WritingMode = WritingMode.Vertical,
            PageWidth = 600,
            PageHeight = 800,
            Margin = 40
        }, m);
        Assert.Single(pages);
        var imgs = pages[0].Ops.OfType<DrawImageOp>().ToList();
        Assert.Equal(2, imgs.Count);
        Assert.True(imgs.All(i => i.Width < 80 && i.Height < 80), $"img size {imgs[0].Width}x{imgs[0].Height}");
        Assert.True(Math.Abs(imgs[0].X - imgs[1].X) < 2f, "gaiji should share a column");
    }

    [Fact]
    public void VerticalLayoutProducesPages()
    {
        var inlines = new InlineNode[]
        {
            new TextRun("これは試験用の文章です。", new TextStyle { FontSizePx = 22 })
        };
        var book = new Book
        {
            FilePath = "x",
            Title = "t",
            Spine =
            [
                new SpineItem
                {
                    Index = 0,
                    Key = "c",
                    Title = "c",
                    Kind = SpineKind.Reflowable,
                    Blocks = [new ParagraphBlock(inlines, 1, TextAlign.Start, false, 0)]
                }
            ]
        };
        using var m = new DummyMeasurer();
        var pages = LayoutEngine.Layout(book, new LayoutSettings
        {
            WritingMode = WritingMode.Vertical,
            PageWidth = 400,
            PageHeight = 300,
            Margin = 20
        }, m);
        Assert.NotEmpty(pages);
        Assert.NotEmpty(pages[0].Ops);
    }

    [Fact]
    public void IllustrationFillsPageHeight()
    {
        var book = new Book
        {
            FilePath = "x",
            Title = "t",
            Spine =
            [
                new SpineItem
                {
                    Index = 0,
                    Key = "c",
                    Title = "c",
                    Kind = SpineKind.Reflowable,
                    Blocks =
                    [
                        new ParagraphBlock([new TextRun("前文", new TextStyle())], 1, TextAlign.Start, false, 0),
                        new IllustrationBlock("big", 600, 900, "插圖"),
                        new ParagraphBlock([new TextRun("後文", new TextStyle())], 1, TextAlign.Start, false, 0)
                    ]
                }
            ]
        };
        using var m = new DummyMeasurer();
        var pages = LayoutEngine.Layout(book, new LayoutSettings
        {
            WritingMode = WritingMode.Horizontal,
            PageWidth = 1200,
            PageHeight = 800,
            Margin = 40
        }, m);
        var pic = Assert.Single(pages, p => p.Ops.OfType<DrawImageOp>().Any());
        var op = pic.Ops.OfType<DrawImageOp>().Single();
        Assert.Equal(800, op.Height, 0.5);
        Assert.InRange(op.Y, -0.5, 0.5);
        Assert.True(pages.Count >= 3);
    }

    [Fact]
    public void GoToIllustrationJumpsToImagePage()
    {
        var book = new Book
        {
            FilePath = "x",
            Title = "t",
            Spine =
            [
                new SpineItem
                {
                    Index = 0,
                    Key = "c",
                    Title = "c",
                    Kind = SpineKind.Reflowable,
                    Blocks =
                    [
                        new ParagraphBlock([new TextRun("前文前文前文", new TextStyle())], 1, TextAlign.Start, false, 0),
                        new IllustrationBlock("big", 600, 900, "插圖"),
                        new ParagraphBlock([new TextRun("後文後文後文", new TextStyle())], 1, TextAlign.Start, false, 0)
                    ]
                }
            ],
            Illustrations =
            [
                new Illustration { ResourceKey = "big", Label = "插圖", SpineIndex = 0, Width = 600, Height = 900 }
            ]
        };
        using var session = new ReaderSession(book);
        using var m = new DummyMeasurer();
        session.Relayout(m);
        session.PageIndex = 0;
        session.GoToIllustration(book.Illustrations[0]);
        var page = session.Pages[session.PageIndex];
        Assert.Contains(page.Ops.OfType<DrawImageOp>(), op => op.ResourceKey == "big");
    }

    [Fact]
    public void CssWritingModeAndTcyClass()
    {
        var css = CssSubset.Parse("p { writing-mode: vertical-rl; } .tcy { -epub-text-combine: horizontal; }");
        var s = css.Apply(new ComputedStyle(), "p", null, ["tcy"], null);
        Assert.Equal(WritingMode.Vertical, s.WritingMode);
        Assert.True(s.CombineUpright);
    }

    [Fact]
    public void TcySpanAndFullwidthDigitsBecomeUpright()
    {
        var css = CssSubset.Parse(".tcy { -webkit-text-combine: horizontal; text-combine-upright: all; }");
        var html = "<html><body><p>ちょうど<span class=\"tcy\">16</span>時</p><p>６月１日の土曜</p></body></html>";
        var conv = new XhtmlConverter(css, _ => null, 500, "c.xhtml", 0, "Yu Mincho", []);
        var blocks = conv.Convert(html).OfType<ParagraphBlock>().ToList();
        var inlines = blocks.SelectMany(b => b.Inlines).ToList();
        Assert.Contains(inlines.OfType<TateChuYoko>(), t => t.Text == "16");
        Assert.Contains(inlines.OfType<TateChuYoko>(), t => t.Text == "6");
        Assert.Contains(inlines.OfType<TateChuYoko>(), t => t.Text == "1");

        using var m = new DummyMeasurer();
        var book = new Book
        {
            FilePath = "x",
            Title = "t",
            Spine =
            [
                new SpineItem
                {
                    Index = 0, Key = "c", Title = "c", Kind = SpineKind.Reflowable,
                    Blocks = [blocks[0]]
                }
            ]
        };
        var pages = LayoutEngine.Layout(book, new LayoutSettings
        {
            WritingMode = WritingMode.Vertical,
            PageWidth = 400,
            PageHeight = 600,
            Margin = 20,
            LineHeight = 1.8f
        }, m);
        var tcy = Assert.Single(pages[0].Ops.OfType<DrawTextOp>(), o => o.UprightCombine && o.Text == "16");
        Assert.Equal(26, tcy.Width, 0.5);
        Assert.Equal(26, tcy.Height, 0.5);
    }

    [Fact]
    public void HeadingsHaveSpaceAround()
    {
        var html = "<html><body><h1>第一章</h1><p>本文。</p><h2>節</h2><p>続く。</p></body></html>";
        var conv = new XhtmlConverter(new CssSubset(), _ => null, 500, "c.xhtml", 0, "Yu Mincho", []);
        var blocks = conv.Convert(html).OfType<ParagraphBlock>().ToList();
        var h1 = Assert.Single(blocks, b => b is { IsHeading: true, HeadingLevel: 1 });
        var h2 = Assert.Single(blocks, b => b is { IsHeading: true, HeadingLevel: 2 });
        Assert.True(h1.SpaceBeforeEm >= 1.4f);
        Assert.True(h1.SpaceAfterEm >= 1.0f);
        Assert.True(h2.SpaceBeforeEm >= 1.1f);
        Assert.True(h2.SpaceAfterEm >= 0.7f);

        using var m = new DummyMeasurer();
        var book = new Book
        {
            FilePath = "x",
            Title = "t",
            Spine =
            [
                new SpineItem
                {
                    Index = 0, Key = "c", Title = "c", Kind = SpineKind.Reflowable,
                    Blocks = [h1, blocks.First(b => !b.IsHeading)]
                }
            ]
        };
        var pages = LayoutEngine.Layout(book, new LayoutSettings
        {
            WritingMode = WritingMode.Horizontal,
            PageWidth = 600,
            PageHeight = 800,
            Margin = 40
        }, m);
        var title = pages[0].Ops.OfType<DrawTextOp>().First();
        Assert.True(title.Y > 40 + 20);
    }

    [Fact]
    public void HeadingCssMarginZeroStillHasSpace()
    {
        var css = CssSubset.Parse("h1, h2, p, body { margin: 0; } h1 { margin-top: 0; margin-bottom: 0; }");
        var conv = new XhtmlConverter(css, _ => null, 500, "c.xhtml", 0, "Yu Mincho", []);
        var blocks = conv.Convert("<html><body><h1>第一章</h1><p>本文。</p></body></html>").OfType<ParagraphBlock>().ToList();
        var h1 = Assert.Single(blocks, b => b.IsHeading);
        Assert.True(h1.SpaceBeforeEm >= 1.4f);
        Assert.True(h1.SpaceAfterEm >= 1.0f);
    }

    [Fact]
    public void VerticalHeadingDoesNotSitAtTopEdge()
    {
        var inlines = new InlineNode[] { new TextRun("第一章", new TextStyle { FontSizePx = 35 }) };
        var h1 = new ParagraphBlock(inlines, 0, TextAlign.Start, true, 1, 1.6f, 1.1f);
        var book = new Book
        {
            FilePath = "x",
            Title = "t",
            Spine =
            [
                new SpineItem
                {
                    Index = 0, Key = "c", Title = "c", Kind = SpineKind.Reflowable,
                    Blocks = [h1]
                }
            ]
        };
        using var m = new DummyMeasurer();
        var pages = LayoutEngine.Layout(book, new LayoutSettings
        {
            WritingMode = WritingMode.Vertical,
            PageWidth = 600,
            PageHeight = 800,
            Margin = 40
        }, m);
        var title = pages[0].Ops.OfType<DrawTextOp>().First();
        Assert.True(title.Y >= 40);
    }

    [Fact]
    public void VerticalHangingIndentStaysOnPage()
    {
        var css = CssSubset.Parse("""
            body { padding: 1em; writing-mode: vertical-rl; }
            .title { font-size: 1.2em; font-weight: bold; }
            .box { padding-top: 3.9em; text-indent: -3.9em; }
            """);
        var html = """
            <html><body>
              <p class="title">目次</p>
              <div class="box">
                <p>一章　常識</p>
                <p>二章　傭兵</p>
              </div>
            </body></html>
            """;
        var conv = new XhtmlConverter(css, _ => null, 500, "c.xhtml", 0, "Yu Mincho", []);
        var blocks = conv.Convert(html).OfType<ParagraphBlock>().ToList();
        var ch = Assert.Single(blocks, b => b.Inlines.OfType<TextRun>().Any(t => t.Text.Contains("一章")));
        Assert.True(ch.IndentEm < -3f);
        Assert.True(ch.PadTopEm >= 3.9f);

        using var m = new DummyMeasurer();
        var book = new Book
        {
            FilePath = "x",
            Title = "t",
            Spine =
            [
                new SpineItem
                {
                    Index = 0, Key = "c", Title = "c", Kind = SpineKind.Reflowable,
                    Blocks = blocks.ToList<BlockNode>()
                }
            ]
        };
        var pages = LayoutEngine.Layout(book, new LayoutSettings
        {
            WritingMode = WritingMode.Vertical,
            PageWidth = 800,
            PageHeight = 600,
            Margin = 40
        }, m);
        Assert.NotEmpty(pages);
        foreach (var op in pages.SelectMany(p => p.Ops).OfType<DrawTextOp>())
            Assert.True(op.Y >= 39.5f, $"「{op.Text}」Y={op.Y}");
    }

    [Fact]
    public void SvgCoverDoesNotInsertBlankPages()
    {
        var html = """
            <html xmlns="http://www.w3.org/1999/xhtml"><body>
            <div class="main">

            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="100%" height="100%" viewBox="0 0 1443 2048">
            <image width="1443" height="2048" xlink:href="../image/cover.jpg"/>
            </svg>

            </div>
            </body></html>
            """;
        var large = BmpWriter.Make(600, 800, 200, 10, 10);
        byte[]? Load(string key) => key.Contains("cover") ? large : null;
        var conv = new XhtmlConverter(new CssSubset(), Load, 500, "xhtml/p-cover.xhtml", 0, "Yu Mincho", []);
        var blocks = conv.Convert(html);
        Assert.Single(blocks);
        Assert.IsType<IllustrationBlock>(blocks[0]);

        var book = new Book
        {
            FilePath = "x",
            Title = "t",
            Spine =
            [
                new SpineItem
                {
                    Index = 0, Key = "c", Title = "c", Kind = SpineKind.Image,
                    ImageKey = "image/cover.jpg", ImageWidth = 600, ImageHeight = 800
                }
            ]
        };
        using var m = new DummyMeasurer();
        var pages = LayoutEngine.Layout(book, new LayoutSettings
        {
            WritingMode = WritingMode.Vertical,
            PageWidth = 900,
            PageHeight = 1200,
            Margin = 40
        }, m);
        Assert.Single(pages);
        Assert.True(pages[0].IsImagePage);
        Assert.Single(pages[0].Ops.OfType<DrawImageOp>());
    }

    [Fact]
    public void VerticalPunctMapsDashAndEllipsis()
    {
        Assert.Equal("︱", VerticalPunct.Apply("ー"));
        Assert.Equal("︙", VerticalPunct.Apply("…"));
        Assert.Equal("︵", VerticalPunct.Apply("（"));
        Assert.Equal("春", VerticalPunct.Apply("春"));
    }

    [Fact]
    public void VerticalRubyDoesNotInflateColumnPitch()
    {
        var style = new TextStyle { FontSizePx = 22 };
        using var m = new DummyMeasurer();
        var settings = new LayoutSettings
        {
            WritingMode = WritingMode.Vertical,
            PageWidth = 800,
            PageHeight = 600,
            Margin = 40,
            LineHeight = 1.8f
        };
        List<PageModel> Pages(params InlineNode[] inlines)
        {
            var book = new Book
            {
                FilePath = "x",
                Title = "t",
                Spine =
                [
                    new SpineItem
                    {
                        Index = 0, Key = "c", Title = "c", Kind = SpineKind.Reflowable,
                        Blocks = [new ParagraphBlock(inlines, 0, TextAlign.Start, false, 0)]
                    }
                ]
            };
            return LayoutEngine.Layout(book, settings, m);
        }

        float Pitch(IEnumerable<DrawOp> ops)
        {
            var xs = ops.Select(o => o switch
            {
                DrawTextOp t => t.X,
                DrawRubyOp r => r.X,
                _ => float.NaN
            }).Where(x => !float.IsNaN(x)).OrderByDescending(x => x).Take(2).ToList();
            Assert.Equal(2, xs.Count);
            return xs[0] - xs[1];
        }

        var plain = Pitch(Pages(new TextRun("春", style), new LineBreakInline(), new TextRun("夏", style))[0].Ops);
        var rubied = Pitch(Pages(
            new RubyInline("春", "はる", style),
            new LineBreakInline(),
            new RubyInline("夏", "なつ", style))[0].Ops);
        Assert.InRange(plain, 22 * 1.5f, 22 * 2.1f);
        Assert.True(Math.Abs(rubied - plain) < 8f, $"plain={plain} ruby={rubied}");
    }

    [Fact]
    public void VerticalRubyKeepsBaseOnTheGrid()
    {
        var style = new TextStyle { FontSizePx = 22 };
        using var m = new DummyMeasurer();
        var settings = new LayoutSettings
        {
            WritingMode = WritingMode.Vertical,
            PageWidth = 800,
            PageHeight = 600,
            Margin = 40,
            LineHeight = 1.8f
        };
        List<PageModel> Pages(params InlineNode[] inlines)
        {
            var book = new Book
            {
                FilePath = "x",
                Title = "t",
                Spine =
                [
                    new SpineItem
                    {
                        Index = 0, Key = "c", Title = "c", Kind = SpineKind.Reflowable,
                        Blocks = [new ParagraphBlock(inlines, 0, TextAlign.Start, false, 0)]
                    }
                ]
            };
            return LayoutEngine.Layout(book, settings, m);
        }

        var plain = Assert.Single(Pages(new TextRun("世", style))[0].Ops.OfType<DrawTextOp>());
        var ruby = Assert.Single(Pages(new RubyInline("世", "せかい", style))[0].Ops.OfType<DrawRubyOp>());
        Assert.Equal(plain.X, ruby.X, 0.5);
        Assert.Equal(plain.Y, ruby.Y, 0.5);
        Assert.True(Math.Abs(ruby.Height - plain.Height) < 4f, $"plainH={plain.Height} rubyH={ruby.Height}");
        Assert.True(ruby.Width <= 22 * 1.2f, $"ruby cell width {ruby.Width}");
    }

    [Fact]
    public void VerticalRubyDoesNotDoubleAdvance()
    {
        var ruby = new RubyInline("春", "はる", new TextStyle { FontSizePx = 22 });
        var book = new Book
        {
            FilePath = "x",
            Title = "t",
            Spine =
            [
                new SpineItem
                {
                    Index = 0, Key = "c", Title = "c", Kind = SpineKind.Reflowable,
                    Blocks = [new ParagraphBlock([ruby], 0, TextAlign.Start, false, 0)]
                }
            ]
        };
        using var m = new DummyMeasurer();
        var pages = LayoutEngine.Layout(book, new LayoutSettings
        {
            WritingMode = WritingMode.Vertical,
            PageWidth = 400,
            PageHeight = 300,
            Margin = 20
        }, m);
        var op = Assert.Single(pages[0].Ops.OfType<DrawRubyOp>());
        Assert.True(op.Height <= 22 * 1.4f, $"ruby height {op.Height}");
    }

    [Fact]
    public void VerticalPeriodDoesNotStartAColumn()
    {
        var style = new TextStyle { FontSizePx = 26 };
        var book = new Book
        {
            FilePath = "x",
            Title = "t",
            Spine =
            [
                new SpineItem
                {
                    Index = 0, Key = "c", Title = "c", Kind = SpineKind.Reflowable,
                    Blocks = [new ParagraphBlock([new TextRun("一二三四五六七八。九", style)], 0, TextAlign.Start, false, 0)]
                }
            ]
        };
        using var m = new DummyMeasurer();
        var pages = LayoutEngine.Layout(book, new LayoutSettings
        {
            WritingMode = WritingMode.Vertical,
            PageWidth = 400,
            PageHeight = 288,
            Margin = 40,
            LineHeight = 1.8f
        }, m);
        var texts = pages.SelectMany(p => p.Ops).OfType<DrawTextOp>().ToList();
        var eight = Assert.Single(texts, t => t.Text == "八");
        var period = Assert.Single(texts, t => t.Text is "。" or "︒");
        var nine = Assert.Single(texts, t => t.Text == "九");
        Assert.Equal(eight.X, period.X, 0.5);
        Assert.True(period.Y > eight.Y, $"period Y={period.Y} eight Y={eight.Y}");
        Assert.True(nine.X < eight.X, $"nine should be next column nine.X={nine.X} eight.X={eight.X}");
    }

    [Fact]
    public void VerticalOpeningQuoteMovesWithNextChar()
    {
        var style = new TextStyle { FontSizePx = 26 };
        var book = new Book
        {
            FilePath = "x",
            Title = "t",
            Spine =
            [
                new SpineItem
                {
                    Index = 0, Key = "c", Title = "c", Kind = SpineKind.Reflowable,
                    Blocks = [new ParagraphBlock([new TextRun("一二三四五六七「八", style)], 0, TextAlign.Start, false, 0)]
                }
            ]
        };
        using var m = new DummyMeasurer();
        var pages = LayoutEngine.Layout(book, new LayoutSettings
        {
            WritingMode = WritingMode.Vertical,
            PageWidth = 400,
            PageHeight = 288,
            Margin = 40,
            LineHeight = 1.8f
        }, m);
        var texts = pages.SelectMany(p => p.Ops).OfType<DrawTextOp>().ToList();
        var seven = Assert.Single(texts, t => t.Text == "七");
        var quote = Assert.Single(texts, t => t.Text is "「" or "﹁");
        var eight = Assert.Single(texts, t => t.Text == "八");
        Assert.Equal(quote.X, eight.X, 0.5);
        Assert.True(quote.X < seven.X, $"quote should start next column quote.X={quote.X} seven.X={seven.X}");
    }

    [Fact]
    public void DenshokyoEnglishClassesApplyOnToc()
    {
        var css = CssSubset.Parse("""
            .vrtl { -epub-writing-mode: vertical-rl; }
            .font-0em90 { font-size: 0.90em; }
            .font-1em10 { font-size: 1.10em; }
            .font-1em30 { font-size: 1.30em; }
            .vrtl .start-2em { margin-top: 2.00em; }
            .hltr .start-2em { margin-left: 2.00em; }
            """);
        var html = """
            <html class="vrtl"><body class="p-toc">
            <div class="main start-2em">
            <p><span class="mfont font-1em30">CONTENTS</span></p>
            <p><span class="mfont font-1em10">第一章</span><br/><span class="mfont font-0em90">"His birthday (I)"</span></p>
            </div></body></html>
            """;
        var conv = new XhtmlConverter(css, _ => null, 500, "xhtml/p-toc-001.xhtml", 0, "Yu Mincho", []);
        var blocks = conv.Convert(html).OfType<ParagraphBlock>().ToList();
        Assert.True(blocks.Count >= 2, $"blocks={blocks.Count}");
        Assert.Contains(blocks, b => b.PadTopEm >= 1.9f);

        var contents = blocks.SelectMany(b => b.Inlines).OfType<TextRun>()
            .First(t => t.Text.Contains("CONTENTS"));
        Assert.InRange(contents.Style.FontSizePx, 26 * 1.25f, 26 * 1.35f);

        var en = blocks.SelectMany(b => b.Inlines).OfType<TextRun>()
            .First(t => t.Text.Contains("birthday"));
        Assert.InRange(en.Style.FontSizePx, 26 * 0.85f, 26 * 0.95f);
        Assert.Contains("His birthday", en.Text);

        using var m = new DummyMeasurer();
        var book = new Book
        {
            FilePath = "x",
            Title = "t",
            Spine =
            [
                new SpineItem
                {
                    Index = 0, Key = "c", Title = "c", Kind = SpineKind.Reflowable,
                    Blocks = blocks.ToList<BlockNode>()
                }
            ]
        };
        var pages = LayoutEngine.Layout(book, new LayoutSettings
        {
            WritingMode = WritingMode.Vertical,
            PageWidth = 800,
            PageHeight = 1200,
            Margin = 40,
            LineHeight = 1.8f
        }, m);
        var ops = pages.SelectMany(p => p.Ops).OfType<DrawTextOp>().ToList();
        Assert.DoesNotContain(ops, o => o.UprightCombine && o.Text is "His" or "I");
        Assert.Contains(ops, o => !o.UprightCombine && o.Text.Contains("birthday"));
    }

    [Fact]
    public void KadokawaColophonPaddingDoesNotIndentBodyText()
    {
        var css = CssSubset.Parse("""
            body,div,p { text-indent: 0; }
            .p-colophon .main { padding: 2em 1em 1em 1em; }
            .p-titlepage .author p { margin: 0.5em 0 0 0; }
            """);
        var body = """
            <html class="vrtl"><body class="p-text"><div class="main">
            <p>　食卓に並んでいるのは手料理が半分。</p>
            <p>「うーん、おいしー♪」</p>
            </div></body></html>
            """;
        var conv = new XhtmlConverter(css, _ => null, 500, "xhtml/p-001.xhtml", 0, "Yu Mincho", []);
        var paras = conv.Convert(body).OfType<ParagraphBlock>().ToList();
        Assert.Equal(2, paras.Count);
        Assert.All(paras, p =>
        {
            Assert.True(p.PadTopEm < 0.01f, $"PadTopEm={p.PadTopEm}");
            Assert.True(p.IndentEm < 0.01f, $"IndentEm={p.IndentEm}");
            Assert.True(p.SpaceBeforeEm < 0.01f, $"SpaceBeforeEm={p.SpaceBeforeEm}");
        });

        var colo = """
            <html class="vrtl"><body class="p-colophon"><div class="main">
            <p>奥付</p>
            </div></body></html>
            """;
        var coloParas = new XhtmlConverter(css, _ => null, 500, "xhtml/p-colophon.xhtml", 0, "Yu Mincho", [])
            .Convert(colo).OfType<ParagraphBlock>().ToList();
        Assert.True(coloParas[0].PadTopEm >= 1.9f, $"colophon PadTopEm={coloParas[0].PadTopEm}");
    }

    [Fact]
    public void InBookTocLinksJumpToFragment()
    {
        var toc = """
            <html class="vrtl"><body class="p-toc"><div class="main">
            <p><a href="p-002.xhtml#id-a001"><span>Episode1.　コミイチ</span></a></p>
            </div></body></html>
            """;
        var chap = """
            <html class="vrtl"><body class="p-text"><div class="main">
            <p>前文。</p>
            <p id="id-a001">本章標題</p>
            <p>本文が続く。</p>
            </div></body></html>
            """;
        var tocBlocks = new XhtmlConverter(new CssSubset(), _ => null, 500, "item/xhtml/p-toc-001.xhtml", 0, "Yu Mincho", []).Convert(toc);
        var chapBlocks = new XhtmlConverter(new CssSubset(), _ => null, 500, "item/xhtml/p-002.xhtml", 1, "Yu Mincho", []).Convert(chap);
        var hrefs = tocBlocks.OfType<ParagraphBlock>().SelectMany(p => p.Inlines)
            .Select(n => n switch
            {
                TextRun t => t.Href,
                TateChuYoko t => t.Href,
                RubyInline t => t.Href,
                _ => null
            })
            .Where(h => !string.IsNullOrEmpty(h))
            .ToList();
        Assert.Contains("p-002.xhtml#id-a001", hrefs);
        var title = chapBlocks.OfType<ParagraphBlock>().First(p => p.Inlines.OfType<TextRun>().Any(t => t.Text.Contains("本章標題")));
        Assert.Equal("id-a001", title.AnchorId);

        var book = new Book
        {
            FilePath = "x",
            Title = "t",
            Spine =
            [
                new SpineItem { Index = 0, Key = "item/xhtml/p-toc-001.xhtml", Title = "toc", Kind = SpineKind.Reflowable, Blocks = tocBlocks },
                new SpineItem { Index = 1, Key = "item/xhtml/p-002.xhtml", Title = "ch", Kind = SpineKind.Reflowable, Blocks = chapBlocks }
            ]
        };
        using var session = new ReaderSession(book);
        session.Relayout(new DummyMeasurer());
        Assert.True(session.GoToHref("p-002.xhtml#id-a001", "item/xhtml/p-toc-001.xhtml"));
        Assert.Equal(1, session.Pages[session.PageIndex].SpineIndex);
        Assert.Contains(session.Pages[session.PageIndex].Ops.OfType<DrawTextOp>(), o => o.Text.Contains("本"));
    }
}
