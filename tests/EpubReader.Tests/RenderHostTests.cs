using EpubReader.Core;
using EpubReader.Rendering;

namespace EpubReader.Tests;

public class RenderHostTests
{
    [Fact]
    public void VerticalCjkProducesInkNotJustDots()
    {
        using var host = new RenderHost("Microsoft JhengHei", "ja");
        var style = new TextStyle
        {
            FontSizePx = 22,
            FontFamily = host.FontFamily,
            ColorArgb = 0xFF000000
        };
        var m = host.Measure("春", style, WritingMode.Vertical);
        Assert.True(m.Width <= style.FontSizePx * 1.6f, $"春 Width={m.Width} Height={m.Height} Advance={m.Advance} em={style.FontSizePx}");
        Assert.InRange(m.Height, 8, 80);

        var page = new PageModel();
        page.Ops.Add(new DrawTextOp("春夏秋冬本文", 120, 40, m.Width, 200, style, WritingMode.Vertical, false));
        var settings = new LayoutSettings
        {
            WritingMode = WritingMode.Vertical,
            PageWidth = 240,
            PageHeight = 320,
            PaperArgb = 0xFFFFFFFF,
            InkArgb = 0xFF000000
        };
        var rendered = host.Render([page], settings, _ => null, 1f);
        int ink = CountInk(rendered.Bgra);
        Assert.True(ink > 80, $"直排應畫出文字，墨點只有 {ink}");
    }

    [Fact]
    public void HorizontalSpreadPutsFirstPageOnTheLeft()
    {
        using var host = new RenderHost("Microsoft JhengHei", "ja");
        var red = PngWriter.Make(20, 20, 200, 10, 10);
        var blue = PngWriter.Make(20, 20, 10, 10, 200);
        var first = new PageModel { IsImagePage = true };
        first.Ops.Add(new DrawImageOp("red", 0, 0, 20, 20));
        var second = new PageModel { IsImagePage = true };
        second.Ops.Add(new DrawImageOp("blue", 0, 0, 20, 20));
        byte[]? Load(string k) => k == "red" ? red : blue;
        var settings = new LayoutSettings
        {
            WritingMode = WritingMode.Horizontal,
            VisualLtr = true,
            PageWidth = 200,
            PageHeight = 100,
            PaperArgb = 0xFFFFFFFF
        };
        var rendered = host.Render([first, second], settings, Load, 1f);
        Assert.True(Channel(rendered, 40, 50, 2) > Channel(rendered, 40, 50, 0) + 40, "橫排左半應是第一頁（紅）");
        Assert.True(Channel(rendered, 160, 50, 0) > Channel(rendered, 160, 50, 2) + 40, "橫排右半應是第二頁（藍）");
    }

    [Fact]
    public void VerticalSpreadPutsFirstPageOnTheRight()
    {
        using var host = new RenderHost("Microsoft JhengHei", "ja");
        var red = PngWriter.Make(20, 20, 200, 10, 10);
        var blue = PngWriter.Make(20, 20, 10, 10, 200);
        var first = new PageModel { IsImagePage = true };
        first.Ops.Add(new DrawImageOp("red", 0, 0, 20, 20));
        var second = new PageModel { IsImagePage = true };
        second.Ops.Add(new DrawImageOp("blue", 0, 0, 20, 20));
        byte[]? Load(string k) => k == "red" ? red : blue;
        var settings = new LayoutSettings
        {
            WritingMode = WritingMode.Vertical,
            VisualLtr = false,
            PageWidth = 200,
            PageHeight = 100,
            PaperArgb = 0xFFFFFFFF
        };
        var rendered = host.Render([first, second], settings, Load, 1f);
        Assert.True(Channel(rendered, 160, 50, 2) > Channel(rendered, 160, 50, 0) + 40, "直排右半應是第一頁（紅）");
        Assert.True(Channel(rendered, 40, 50, 0) > Channel(rendered, 40, 50, 2) + 40, "直排左半應是第二頁（藍）");
    }

    static int Channel(RenderedPage page, int x, int y, int bgraIndex)
    {
        int i = (y * page.Width + x) * 4 + bgraIndex;
        return page.Bgra[i];
    }

    static int CountInk(byte[] bgra)
    {
        int n = 0;
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i] < 200 || bgra[i + 1] < 200 || bgra[i + 2] < 200)
                n++;
        }
        return n;
    }
}
