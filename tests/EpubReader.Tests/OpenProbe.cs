using EpubReader.Core;

namespace EpubReader.Tests;

public class OpenProbe
{
    [Fact]
    public void FourteenYearOldOpensVerticalNotManga()
    {
        var path = Path.Combine(RepoRoot(), "..", "AeroEpubViewer", "１４歳とイラストレーター 8【電子特典付き】.epub");
        if (!File.Exists(path))
            path = @"D:\git\AeroEpubViewer\１４歳とイラストレーター 8【電子特典付き】.epub";
        if (!File.Exists(path))
            return;
        using var book = BookLoader.Open(path, 500);
        Assert.False(book.IsManga);
        Assert.False(book.IsFixedLayout);
        Assert.Equal(PageProgression.Rtl, book.Progression);
        Assert.Equal(WritingMode.Vertical, book.DefaultWritingMode);
        Assert.Contains(book.Spine, s => s.Kind == SpineKind.Reflowable);
        var p6 = book.Spine.First(s => s.Key.Contains("p-006", StringComparison.OrdinalIgnoreCase));
        Assert.True(
            p6.Blocks.OfType<ParagraphBlock>().Sum(p => p.Inlines.OfType<InlineImage>().Count()) >= 8,
            "p-006 gaiji should stay inline");
        Assert.DoesNotContain(p6.Blocks.OfType<IllustrationBlock>(), b => b.ResourceKey.Contains("gaiji", StringComparison.OrdinalIgnoreCase));
        var toc = book.Spine.First(s => s.Key.Contains("toc", StringComparison.OrdinalIgnoreCase));
        var hrefs = toc.Blocks.OfType<ParagraphBlock>()
            .SelectMany(p => p.Inlines.OfType<TextRun>())
            .Select(t => t.Href)
            .Where(h => !string.IsNullOrEmpty(h))
            .ToList();
        Assert.Contains(hrefs, h => h!.Contains("p-002.xhtml") && h.Contains("id-a001"));
        var session = new ReaderSession(book);
        session.Relayout(new DummyMeasurer());
        Assert.True(session.GoToHref("p-002.xhtml#id-a001", toc.Key));
        Assert.Equal(
            book.Spine.First(s => s.Key.Contains("p-002", StringComparison.OrdinalIgnoreCase)).Index,
            session.Pages[session.PageIndex].SpineIndex);
    }

    [Fact]
    public void OpensOmfMangaWithImageSpine()
    {
        var path = Path.Combine(RepoRoot(), "バイオーグ・トリニティ 13.epub");
        if (!File.Exists(path))
            path = @"D:\git\AeroEpubViewer\バイオーグ・トリニティ 13.epub";
        if (!File.Exists(path))
            path = @"D:\git\epubreader\バイオーグ・トリニティ 13.epub";
        if (!File.Exists(path))
            return;
        using var book = BookLoader.Open(path, 500);
        Assert.True(book.IsManga);
        Assert.False(book.IsFixedLayout);
        Assert.Equal(PageProgression.Rtl, book.Progression);
        Assert.Equal(WritingMode.Vertical, book.DefaultWritingMode);
        Assert.True(book.Spine.Count >= 100, $"spine={book.Spine.Count}");
        Assert.All(book.Spine, s => Assert.Equal(SpineKind.Image, s.Kind));
        Assert.NotNull(book.LoadResource);
        var first = book.Spine[0];
        Assert.NotNull(first.ImageKey);
        var bytes = book.LoadResource!(first.ImageKey!);
        Assert.NotNull(bytes);
        Assert.True(bytes!.Length > 1000);
    }

    [Fact]
    public void GochiusaOpensVerticalAndPairsRightLeft()
    {
        var path = Path.Combine(RepoRoot(), "..", "AeroEpubViewer",
            "[Ｋｏｉ] ご注文はうさぎですか？　１２巻【特典付き】 (まんがタイムＫＲコミックス).epub");
        if (!File.Exists(path))
            path = @"D:\git\AeroEpubViewer\[Ｋｏｉ] ご注文はうさぎですか？　１２巻【特典付き】 (まんがタイムＫＲコミックス).epub";
        if (!File.Exists(path))
            return;
        using var book = BookLoader.Open(path, 500);
        Assert.False(book.PairLeftToRight);
        Assert.Equal(PageProgression.Rtl, book.Progression);
        Assert.Equal(WritingMode.Vertical, book.DefaultWritingMode);
        Assert.True(book.HasPageSpreads);
        using var session = new ReaderSession(book);
        session.Relayout(new DummyMeasurer());
        Assert.True(session.Pages.Count >= 5, $"pages={session.Pages.Count}");
        Assert.Equal(PageSpreadKind.Right, session.Pages[3].Spread);
        Assert.Equal(PageSpreadKind.Left, session.Pages[4].Spread);
        session.PageIndex = 3;
        Assert.Equal([3, 4], session.VisiblePageIndices());
    }

    [Fact]
    public void C3ParagraphIndentIsNotStacked()
    {
        var path = Path.Combine(RepoRoot(), "..", "AeroEpubViewer", "C3 ―シーキューブ― XV.epub");
        if (!File.Exists(path))
            path = @"D:\git\AeroEpubViewer\C3 ―シーキューブ― XV.epub";
        if (!File.Exists(path))
            return;
        using var book = BookLoader.Open(path, 500);
        var paras = book.Spine
            .Where(s => s.Kind == SpineKind.Reflowable)
            .SelectMany(s => s.Blocks.OfType<ParagraphBlock>())
            .Where(p => p.Inlines.OfType<TextRun>().Any(t => t.Text.Contains("食卓")))
            .Take(8)
            .ToList();
        Assert.True(paras.Count > 0, "C3 body paragraphs not found");
        var dump = string.Join(" | ", paras.Select(p =>
        {
            var t = string.Concat(p.Inlines.OfType<TextRun>().Select(x => x.Text));
            if (t.Length > 16) t = t[..16];
            return $"ind={p.IndentEm:0.##} pad={p.PadTopEm:0.##} '{t}'";
        }));
        Assert.True(paras.All(p => p.IndentEm <= 0.01f && p.PadTopEm <= 0.01f), dump);

        var sample = paras.First(p => p.Inlines.OfType<TextRun>().Any(t => t.Text.Contains("食卓")));
        var head = string.Concat(sample.Inlines.OfType<TextRun>().Select(t => t.Text));
        var codes = string.Join(" ", head.Take(4).Select(c => $"U+{(int)c:X4}"));
        using var measurer = new DummyMeasurer();
        var pages = LayoutEngine.Layout(new Book
        {
            FilePath = "x",
            Title = "t",
            Spine =
            [
                new SpineItem
                {
                    Index = 0, Key = "c", Title = "c", Kind = SpineKind.Reflowable,
                    Blocks = [sample]
                }
            ]
        }, new LayoutSettings
        {
            WritingMode = WritingMode.Vertical,
            PageWidth = 800,
            PageHeight = 1200,
            Margin = 44,
            FontScale = 1
        }, measurer);
        var ops = pages[0].Ops.OfType<DrawTextOp>().ToList();
        var first = ops[0];
        var shoku = ops.First(o => o.Text.Contains("食"));
        float em = first.Style.FontSizePx;
        Assert.True(shoku.Y - 44 < em * 1.15f,
            $"{codes} first='{first.Text}' Y={first.Y} 食Y={shoku.Y} em={em} indent={sample.IndentEm} pad={sample.PadTopEm}");
    }

    static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (File.Exists(Path.Combine(dir, "EpubReader.sln")) || Directory.Exists(Path.Combine(dir, "src")))
                return dir;
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}
