using EpubReader.Core;

namespace EpubReader.Tests;

public class ImageSizeReaderTests
{
    [Fact]
    public void ReadsPngHeader()
    {
        var png = PngWriter.Make(32, 24, 10, 20, 30);
        var size = ImageSizeReader.TryRead(png);
        Assert.NotNull(size);
        Assert.Equal(32, size!.Value.Width);
        Assert.Equal(24, size.Value.Height);
    }

    [Fact]
    public void ReadsBmpHeader()
    {
        var bmp = BmpWriter.Make(640, 480, 255, 0, 0);
        var size = ImageSizeReader.TryRead(bmp);
        Assert.NotNull(size);
        Assert.Equal(640, size!.Value.Width);
        Assert.Equal(480, size.Value.Height);
    }
}
