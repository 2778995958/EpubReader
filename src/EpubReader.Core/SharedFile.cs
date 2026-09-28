namespace EpubReader.Core;

/// <summary>
/// 開檔時允許別人讀寫／刪除，避免閱讀器把書檔鎖死。
/// </summary>
static class SharedFile
{
    public const FileShare Share = FileShare.ReadWrite | FileShare.Delete;

    public static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, Share, 64 * 1024);

    public static MemoryStream ReadAll(string path)
    {
        using var fs = OpenRead(path);
        var ms = fs.Length <= int.MaxValue
            ? new MemoryStream((int)fs.Length)
            : new MemoryStream();
        fs.CopyTo(ms);
        ms.Position = 0;
        return ms;
    }

    public sealed class Holding : IDisposable
    {
        readonly IDisposable[] _items;

        public Holding(params IDisposable[] items) => _items = items;

        public void Dispose()
        {
            for (int i = _items.Length - 1; i >= 0; i--)
            {
                try { _items[i].Dispose(); }
                catch { /* ignore */ }
            }
        }
    }
}
