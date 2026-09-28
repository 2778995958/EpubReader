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
