using SharpCompress.Archives;
using SharpCompress.Readers;

namespace EpubReader.Core;

public static class ImageArchiveLoader
{
    static readonly HashSet<string> ArchiveExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".cbz", ".7z", ".cb7", ".rar", ".cbr", ".tar", ".cbt"
    };

    public static bool IsArchive(string path) => ArchiveExt.Contains(Path.GetExtension(path));

    public static bool IsImageFolder(string path) =>
        Directory.Exists(path) && Directory.EnumerateFiles(path).Any(f => ImageSizeReader.IsImageExtension(Path.GetExtension(f)));

    public static Book Open(string path, int imageThreshold)
    {
        List<(string Name, byte[] Data, ImageSize Size)> pages;
        IDisposable lifetime;
        if (Directory.Exists(path))
        {
            pages = LoadFolder(path, imageThreshold);
            lifetime = new NopDisposable();
        }
        else
        {
            var loaded = LoadArchive(path, imageThreshold);
            pages = loaded.Pages;
            lifetime = loaded.Lifetime;
        }

        if (pages.Count == 0)
            throw new InvalidOperationException("壓縮檔或資料夾裡沒有超過門檻的圖片。");

        var illustrations = new List<Illustration>();
        var spine = new List<SpineItem>();
        byte[]? Load(string key)
        {
            var p = pages.FirstOrDefault(x => x.Name == key);
            return p.Data;
        }

        for (int i = 0; i < pages.Count; i++)
        {
            var p = pages[i];
            spine.Add(new SpineItem
            {
                Index = i,
                Key = p.Name,
                Title = Path.GetFileName(p.Name),
                Kind = SpineKind.Image,
                Spread = i == 0 ? PageSpreadKind.Center : (i % 2 == 1 ? PageSpreadKind.Right : PageSpreadKind.Left),
                ImageKey = p.Name,
                ImageWidth = p.Size.Width,
                ImageHeight = p.Size.Height
            });
            illustrations.Add(new Illustration
            {
                ResourceKey = p.Name,
                Label = Path.GetFileName(p.Name),
                SpineIndex = i,
                Width = p.Size.Width,
                Height = p.Size.Height
            });
        }

        var title = Directory.Exists(path) ? new DirectoryInfo(path).Name : Path.GetFileNameWithoutExtension(path);
        return new Book
        {
            FilePath = path,
            Title = title,
            Language = "ja",
            DefaultWritingMode = WritingMode.Vertical,
            Progression = PageProgression.Rtl,
            IsManga = true,
            IsFixedLayout = false,
            HasPageSpreads = true,
            PairLeftToRight = false,
            Spine = spine,
            Toc = spine.Select(s => new TocItem { Title = s.Title, SpineIndex = s.Index }).ToList(),
            Illustrations = illustrations,
            LoadResource = Load,
            Lifetime = lifetime,
            ImageThreshold = imageThreshold
        };
    }

    static List<(string Name, byte[] Data, ImageSize Size)> LoadFolder(string path, int threshold)
    {
        var files = Directory.EnumerateFiles(path)
            .Where(f => ImageSizeReader.IsImageExtension(Path.GetExtension(f)))
            .OrderBy(f => Path.GetFileName(f), Comparer<string>.Create(NaturalSort.Compare))
            .ToList();
        var pages = new List<(string, byte[], ImageSize)>();
        foreach (var f in files)
        {
            if (Path.GetFileName(f).StartsWith('.')) continue;
            var data = File.ReadAllBytes(f);
            var size = ImageSizeReader.TryRead(data);
            if (size == null) continue;
            if (Math.Max(size.Value.Width, size.Value.Height) < threshold) continue;
            pages.Add((Path.GetFileName(f), data, size.Value));
        }
        return pages;
    }

    static (List<(string Name, byte[] Data, ImageSize Size)> Pages, IDisposable Lifetime) LoadArchive(string path, int threshold)
    {
        var pages = new List<(string Name, byte[] Data, ImageSize Size)>();
        using var fs = SharedFile.OpenRead(path);
        using var archive = ArchiveFactory.OpenArchive(fs, new ReaderOptions());
        var named = archive.Entries
            .Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key))
            .Select(e => (Entry: e, Name: e.Key!.Replace('\\', '/')))
            .Where(x =>
                !x.Name.Contains("/__MACOSX/", StringComparison.OrdinalIgnoreCase)
                && !x.Name.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(x.Name).StartsWith('.')
                && ImageSizeReader.IsImageExtension(Path.GetExtension(x.Name)))
            .OrderBy(x => x.Name, Comparer<string>.Create(NaturalSort.Compare))
            .ToList();

        foreach (var (entry, name) in named)
        {
            using var stream = entry.OpenEntryStream();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            var data = copy.ToArray();
            var size = ImageSizeReader.TryRead(data);
            if (size == null) continue;
            if (Math.Max(size.Value.Width, size.Value.Height) < threshold) continue;
            pages.Add((name, data, size.Value));
        }
        return (pages, new NopDisposable());
    }

    sealed class NopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
