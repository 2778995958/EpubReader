namespace EpubReader.Core;

public static class BookLoader
{
    public static Book Open(string path, int imageThreshold = 500)
    {
        if (Directory.Exists(path))
            return ImageArchiveLoader.Open(path, imageThreshold);
        if (!File.Exists(path))
            throw new FileNotFoundException("找不到檔案。", path);

        var ext = Path.GetExtension(path);
        if (ext.Equals(".epub", StringComparison.OrdinalIgnoreCase))
            return EpubBookLoader.Open(path, imageThreshold);
        if (ImageArchiveLoader.IsArchive(path))
            return ImageArchiveLoader.Open(path, imageThreshold);
        throw new InvalidOperationException("只支援 EPUB、CBZ/ZIP/7Z/RAR 漫畫壓縮檔，或圖片資料夾。");
    }
}
