using System.IO.Compression;
using System.Text;
using EpubReader.Core;

namespace EpubReader.Tests;

public class FixtureEpubTests
{
    [Fact]
    public void OpensMinimalEpub3()
    {
        var path = Path.Combine(Path.GetTempPath(), "epubreader-min-" + Guid.NewGuid().ToString("N") + ".epub");
        try
        {
            File.WriteAllBytes(path, BuildEpub());
            using var book = BookLoader.Open(path, 500);
            Assert.Equal("試験", book.Title);
            Assert.NotEmpty(book.Spine);
            Assert.Contains(book.Illustrations, i => i.Width >= 500);
            Assert.DoesNotContain(book.Illustrations, i => i.Width < 500);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void OpensCbz()
    {
        var path = Path.Combine(Path.GetTempPath(), "epubreader-min-" + Guid.NewGuid().ToString("N") + ".cbz");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Add(zip, "page1.bmp", BmpWriter.Make(600, 800, 10, 10, 200));
                Add(zip, "page10.bmp", BmpWriter.Make(600, 800, 10, 200, 10));
                Add(zip, "page2.bmp", BmpWriter.Make(600, 800, 200, 10, 10));
                Add(zip, "icon.png", PngWriter.Make(16, 16, 1, 1, 1));
            }
            using var book = BookLoader.Open(path, 500);
            Assert.True(book.IsManga);
            Assert.Equal(3, book.Spine.Count);
            Assert.Equal("page1.bmp", book.Spine[0].ImageKey);
            Assert.Equal("page2.bmp", book.Spine[1].ImageKey);
            Assert.Equal("page10.bmp", book.Spine[2].ImageKey);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void OpensOmfStyleImageSpineEpub()
    {
        var path = Path.Combine(Path.GetTempPath(), "epubreader-omf-" + Guid.NewGuid().ToString("N") + ".epub");
        try
        {
            File.WriteAllBytes(path, BuildOmfEpub());
            using (var book = BookLoader.Open(path, 500))
            {
                Assert.True(book.IsManga);
                Assert.False(book.IsFixedLayout);
                Assert.Equal(PageProgression.Rtl, book.Progression);
                Assert.Equal(WritingMode.Vertical, book.DefaultWritingMode);
                Assert.Equal(3, book.Spine.Count);
                Assert.All(book.Spine, s => Assert.Equal(SpineKind.Image, s.Kind));
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void OpeningDoesNotLockSourceFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "epubreader-nolock-" + Guid.NewGuid().ToString("N") + ".epub");
        File.WriteAllBytes(path, BuildEpub());
        try
        {
            using var book = BookLoader.Open(path, 500);
            File.Delete(path);
            Assert.False(File.Exists(path));
            Assert.NotEmpty(book.Spine);
            var key = book.Illustrations[0].ResourceKey;
            var bytes = book.LoadResource!(key);
            Assert.NotNull(bytes);
            Assert.True(bytes!.Length > 100);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    static byte[] BuildEpub()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var s = mime.Open())
                s.Write(Encoding.ASCII.GetBytes("application/epub+zip"));
            Add(zip, "META-INF/container.xml",
                """<?xml version="1.0"?><container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="OPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles></container>""");
            Add(zip, "OPS/content.opf", """
                <?xml version="1.0" encoding="UTF-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" unique-identifier="id" version="3.0">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:identifier id="id">urn:uuid:test</dc:identifier>
                    <dc:title>試験</dc:title>
                    <dc:language>ja</dc:language>
                  </metadata>
                  <manifest>
                    <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
                    <item id="c1" href="c1.xhtml" media-type="application/xhtml+xml"/>
                    <item id="big" href="big.bmp" media-type="image/bmp"/>
                    <item id="small" href="small.png" media-type="image/png"/>
                  </manifest>
                  <spine page-progression-direction="rtl">
                    <itemref idref="c1"/>
                  </spine>
                </package>
                """);
            Add(zip, "OPS/nav.xhtml", """
                <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
                <body><nav epub:type="toc"><ol><li><a href="c1.xhtml">一</a></li></ol></nav></body></html>
                """);
            Add(zip, "OPS/c1.xhtml", """
                <html xmlns="http://www.w3.org/1999/xhtml">
                <head><style>body{writing-mode:vertical-rl;} .tcy{-epub-text-combine:horizontal;}</style></head>
                <body>
                  <h1>第一章</h1>
                  <p>本文<ruby>漢字<rt>かんじ</rt></ruby><span class="tcy">!!</span>。</p>
                  <p><img src="small.png" alt="飾"/><img src="big.bmp" alt="插圖"/></p>
                </body></html>
                """);
            Add(zip, "OPS/big.bmp", BmpWriter.Make(600, 800, 180, 40, 40));
            Add(zip, "OPS/small.png", PngWriter.Make(40, 40, 9, 9, 9));
        }
        return ms.ToArray();
    }

    static byte[] BuildOmfEpub()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var s = mime.Open())
                s.Write(Encoding.ASCII.GetBytes("application/epub+zip"));
            Add(zip, "META-INF/container.xml",
                """<?xml version="1.0"?><container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="OPS/standard.opf" media-type="application/oebps-package+xml"/></rootfiles></container>""");
            Add(zip, "OPS/standard.opf", """
                <?xml version="1.0" encoding="UTF-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" unique-identifier="id" version="3.0">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:identifier id="id">urn:uuid:omf</dc:identifier>
                    <dc:title>OMF漫畫</dc:title>
                    <dc:language>ja</dc:language>
                  </metadata>
                  <manifest>
                    <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
                    <item id="image001" href="images/p1.bmp" media-type="image/bmp"/>
                    <item id="image002" href="images/p2.bmp" media-type="image/bmp"/>
                    <item id="image003" href="images/p3.bmp" media-type="image/bmp"/>
                  </manifest>
                  <spine page-progression-direction="rtl">
                    <itemref idref="image001"/>
                    <itemref idref="image002" properties="page-spread-right"/>
                    <itemref idref="image003" properties="page-spread-left"/>
                  </spine>
                </package>
                """);
            Add(zip, "OPS/nav.xhtml", """
                <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
                <body><nav epub:type="toc"><ol>
                <li><a href="images/p1.bmp">封面</a></li>
                <li><a href="images/p2.bmp">本編</a></li>
                </ol></nav></body></html>
                """);
            Add(zip, "OPS/images/p1.bmp", BmpWriter.Make(600, 800, 10, 10, 200));
            Add(zip, "OPS/images/p2.bmp", BmpWriter.Make(600, 800, 10, 200, 10));
            Add(zip, "OPS/images/p3.bmp", BmpWriter.Make(600, 800, 200, 10, 10));
        }
        return ms.ToArray();
    }

    static void Add(ZipArchive zip, string name, string text)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var s = e.Open();
        using var w = new StreamWriter(s, new UTF8Encoding(false));
        w.Write(text);
    }

    static void Add(ZipArchive zip, string name, byte[] data)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var s = e.Open();
        s.Write(data);
    }
}
