using EpubReader.Core;

namespace EpubReader.Tests;

public class ColorSpreadProbe
{
    [Fact]
    public void FrontColorIllustrationsAreNotSurroundedByBlanks()
    {
        var path = Path.Combine(RepoRoot(), "１４歳とイラストレーター 8【電子特典付き】.epub");
        if (!File.Exists(path))
            return;
        using var book = BookLoader.Open(path, 500);
        Assert.True(book.Spine.Count > 6);
        for (int i = 0; i <= 6; i++)
            Assert.Equal(SpineKind.Image, book.Spine[i].Kind);

        using var m = new DummyMeasurer();
        var pages = LayoutEngine.Layout(book, new LayoutSettings
        {
            WritingMode = WritingMode.Vertical,
            PageWidth = 900,
            PageHeight = 1200,
            Margin = 56
        }, m);
        var front = pages.Take(7).ToList();
        Assert.Equal(7, front.Count);
        Assert.All(front, p =>
        {
            Assert.True(p.IsImagePage);
            Assert.Contains(p.Ops, op => op is DrawImageOp);
            Assert.DoesNotContain(p.Ops, op => op is DrawTextOp t && string.IsNullOrWhiteSpace(t.Text));
        });
    }

    static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src")))
                return dir;
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}
