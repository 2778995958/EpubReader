using System.Text.Json;

namespace EpubReader.Core;

public sealed class AppState
{
    public List<RecentEntry> Recent { get; set; } = [];
    public Dictionary<string, BookProgress> Books { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int ImageThreshold { get; set; } = 500;
    public int Theme { get; set; } = 2;
    public bool ChromeVisible { get; set; }
}

public static class ProgressStore
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EpubReader", "state.json");

    static string LegacyFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AeroEpubViewer2", "state.json");

    public static AppState Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path) && File.Exists(LegacyFilePath))
                path = LegacyFilePath;
            if (!File.Exists(path)) return new AppState();
            var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(path), Json);
            return state ?? new AppState();
        }
        catch
        {
            return new AppState();
        }
    }

    public static void Save(AppState state)
    {
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(state, Json));
    }

    public static string KeyFor(string path)
    {
        long len = 0;
        try { if (File.Exists(path)) len = new FileInfo(path).Length; }
        catch { /* ignore */ }
        return path + "|" + len;
    }

    public static void Remember(AppState state, Book book, BookProgress progress)
    {
        state.Books[KeyFor(book.FilePath)] = progress;
        state.Recent.RemoveAll(r => string.Equals(r.Path, book.FilePath, StringComparison.OrdinalIgnoreCase));
        state.Recent.Insert(0, new RecentEntry
        {
            Path = book.FilePath,
            Title = book.Title,
            OpenedUtc = DateTime.UtcNow
        });
        if (state.Recent.Count > 20)
            state.Recent.RemoveRange(20, state.Recent.Count - 20);
    }
}
