namespace EpubReader.Core;

public sealed class ReaderSession : IDisposable
{
    public Book Book { get; }
    public LayoutSettings Settings { get; }
    public IReadOnlyList<PageModel> Pages { get; private set; } = [];
    public int PageIndex
    {
        get => _pageIndex;
        set => _pageIndex = Pages.Count == 0 ? 0 : Math.Clamp(value, 0, Pages.Count - 1);
    }
    int _pageIndex;

    public ReaderSession(Book book, BookProgress? progress = null)
    {
        Book = book;
        Settings = new LayoutSettings
        {
            WritingMode = book.DefaultWritingMode,
            FontScale = progress is { FontScale: > 0.3f and < 3f } ? progress.FontScale : 1f,
            Spread = book.IsFixedLayout || book.IsManga || book.HasPageSpreads,
            FontFamily = CjkFonts.DefaultFamily(book.Language)
        };
        Settings.VisualLtr = VisualLtr;
        _restoreSpine = progress?.SpineIndex ?? 0;
        _restoreOffset = progress?.CharOffset ?? 0;
    }

    int _restoreSpine;
    int _restoreOffset;

    public void Relayout(ITextMeasurer measurer)
    {
        var pos = CurrentPosition();
        Pages = LayoutEngine.Layout(Book, Settings, measurer);
        PageIndex = FindPage(pos.Spine, pos.Offset);
        _restoreSpine = -1;
    }

    public (int Spine, int Offset) CurrentPosition()
    {
        if (_restoreSpine >= 0) return (_restoreSpine, _restoreOffset);
        if (Pages.Count == 0) return (0, 0);
        var p = Pages[Math.Clamp(PageIndex, 0, Pages.Count - 1)];
        return (p.SpineIndex, p.CharOffset);
    }

    public BookProgress Snapshot()
    {
        var pos = CurrentPosition();
        return new BookProgress
        {
            SpineIndex = pos.Spine,
            CharOffset = pos.Offset,
            FontScale = Settings.FontScale
        };
    }

    public bool NextPage()
    {
        int step = SpreadStep(1);
        if (PageIndex + step >= Pages.Count) return false;
        PageIndex += step;
        return true;
    }

    public bool PrevPage()
    {
        int step = SpreadStep(-1);
        if (PageIndex + step < 0)
        {
            if (PageIndex == 0) return false;
            PageIndex = 0;
            return true;
        }
        PageIndex += step;
        return true;
    }

    public bool NextChapter()
    {
        if (Pages.Count == 0) return false;
        int spine = Pages[Math.Clamp(PageIndex, 0, Pages.Count - 1)].SpineIndex;
        foreach (var s in ChapterSpines())
        {
            if (s > spine)
            {
                GoToSpine(s);
                return true;
            }
        }
        return false;
    }

    public bool PrevChapter()
    {
        if (Pages.Count == 0) return false;
        int spine = Pages[Math.Clamp(PageIndex, 0, Pages.Count - 1)].SpineIndex;
        var ch = ChapterSpines();
        if (ch.Count == 0) return false;
        int cur = -1;
        for (int i = 0; i < ch.Count; i++)
        {
            if (ch[i] <= spine) cur = i;
            else break;
        }
        if (cur < 0) return false;
        int target = cur == 0 ? ch[0] : ch[cur - 1];
        int before = PageIndex;
        GoToSpine(target);
        return PageIndex != before;
    }

    List<int> ChapterSpines()
    {
        var seen = new HashSet<int>();
        var list = new List<int>();
        void Walk(IEnumerable<TocItem> items)
        {
            foreach (var it in items)
            {
                if ((uint)it.SpineIndex < (uint)Book.Spine.Count
                    && Book.Spine[it.SpineIndex].Kind != SpineKind.Image
                    && !IsIllustrationTitle(it.Title)
                    && seen.Add(it.SpineIndex))
                    list.Add(it.SpineIndex);
                if (it.Children.Count > 0)
                    Walk(it.Children);
            }
        }
        Walk(Book.Toc);
        if (list.Count == 0)
        {
            foreach (var s in Book.Spine)
            {
                if (s.Kind != SpineKind.Image)
                    list.Add(s.Index);
            }
        }
        return list;
    }

    static bool IsIllustrationTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        var t = title.Trim();
        return t.Contains("插圖", StringComparison.OrdinalIgnoreCase)
               || t.Contains("插画", StringComparison.OrdinalIgnoreCase)
               || t.Contains("彩圖", StringComparison.OrdinalIgnoreCase)
               || t.Contains("彩图", StringComparison.OrdinalIgnoreCase)
               || t.Contains("イラスト", StringComparison.Ordinal)
               || t.Contains("illustration", StringComparison.OrdinalIgnoreCase);
    }

    public void GoToSpine(int spineIndex)
    {
        spineIndex = Math.Clamp(spineIndex, 0, Math.Max(0, Book.Spine.Count - 1));
        for (int i = 0; i < Pages.Count; i++)
        {
            if (Pages[i].SpineIndex == spineIndex)
            {
                PageIndex = i;
                return;
            }
        }
    }

    public bool GoToHref(string href, string fromKey)
    {
        href = href.Trim();
        if (href.Length == 0 || Href.IsRemote(href)) return false;
        var (file, frag) = Href.SplitHash(href);
        int spine;
        if (file.Length == 0)
        {
            spine = IndexOfSpine(fromKey);
            if (spine < 0 && Pages.Count > 0)
                spine = Pages[Math.Clamp(PageIndex, 0, Pages.Count - 1)].SpineIndex;
            if (spine < 0) return false;
        }
        else
        {
            spine = IndexOfSpine(Href.Combine(fromKey, file));
            if (spine < 0) spine = IndexOfSpine(file);
            if (spine < 0) return false;
        }

        if (frag.Length > 0)
        {
            foreach (var p in Pages)
            {
                if (p.SpineIndex != spine) continue;
                foreach (var a in p.Anchors)
                {
                    if (a.Id.Equals(frag, StringComparison.OrdinalIgnoreCase))
                    {
                        PageIndex = FindPage(spine, a.CharOffset);
                        return true;
                    }
                }
            }
        }
        GoToSpine(spine);
        return true;
    }

    int IndexOfSpine(string key)
    {
        key = key.Replace('\\', '/');
        var name = key.Split('/').Last();
        for (int i = 0; i < Book.Spine.Count; i++)
        {
            var k = Book.Spine[i].Key.Replace('\\', '/');
            if (k.Equals(key, StringComparison.OrdinalIgnoreCase)
                || k.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase)
                || key.EndsWith("/" + k, StringComparison.OrdinalIgnoreCase)
                || k.Equals(name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    public void GoToIllustration(Illustration ill)
    {
        for (int i = 0; i < Pages.Count; i++)
        {
            if (Pages[i].Ops.OfType<DrawImageOp>().Any(op => op.ResourceKey == ill.ResourceKey))
            {
                PageIndex = i;
                return;
            }
        }
        GoToSpine(ill.SpineIndex);
    }

    public bool VisualLtr =>
        Settings.WritingMode == WritingMode.Horizontal || Book.Progression == PageProgression.Ltr;

    public void ToggleWritingMode()
    {
        Settings.WritingMode = Settings.WritingMode == WritingMode.Vertical
            ? WritingMode.Horizontal
            : WritingMode.Vertical;
        Settings.VisualLtr = VisualLtr;
    }

    public IReadOnlyList<int> VisiblePageIndices()
    {
        if (Pages.Count == 0) return [];
        int i = Math.Clamp(PageIndex, 0, Pages.Count - 1);
        if (!Settings.Spread || !Pages[i].IsImagePage)
            return [i];
        if (Pages[i].Spread == PageSpreadKind.Center)
            return [i];
        // 從書首貪婪配對，避免右頁去配下一張左頁、拆掉「左+右」或「右+左」。
        var mate = SpreadMates();
        if (mate[i] >= 0)
        {
            int a = Math.Min(i, mate[i]);
            int b = Math.Max(i, mate[i]);
            return [a, b];
        }
        if (Book.IsManga)
        {
            if (i == 0) return [0];
            if (i % 2 == 1)
            {
                if (i + 1 < Pages.Count && Pages[i + 1].IsImagePage)
                    return [i, i + 1];
                return [i];
            }
            if (Pages[i - 1].IsImagePage)
                return [i - 1, i];
        }
        return [i];
    }

    int[] SpreadMates()
    {
        int n = Pages.Count;
        var mate = new int[n];
        Array.Fill(mate, -1);
        for (int k = 0; k < n; )
        {
            if (k + 1 < n && ComplementarySpread(k, k + 1))
            {
                mate[k] = k + 1;
                mate[k + 1] = k;
                k += 2;
            }
            else k++;
        }
        return mate;
    }

    bool ComplementarySpread(int a, int b)
    {
        if (!Pages[a].IsImagePage || !Pages[b].IsImagePage) return false;
        var x = Pages[a].Spread;
        var y = Pages[b].Spread;
        if (x is PageSpreadKind.None or PageSpreadKind.Center) return false;
        if (y is PageSpreadKind.None or PageSpreadKind.Center) return false;
        return Book.PairLeftToRight
            ? x == PageSpreadKind.Left && y == PageSpreadKind.Right
            : x == PageSpreadKind.Right && y == PageSpreadKind.Left;
    }

    int SpreadStep(int dir)
    {
        if (!Settings.Spread || Pages.Count == 0) return dir;
        int i = Math.Clamp(PageIndex, 0, Pages.Count - 1);
        if (!Pages[i].IsImagePage) return dir;
        var vis = VisiblePageIndices();
        if (vis.Count < 2) return dir;
        if (dir > 0) return vis[^1] - i + 1;
        return vis[0] - i - 1;
    }

    int FindPage(int spine, int offset)
    {
        int best = 0;
        for (int i = 0; i < Pages.Count; i++)
        {
            if (Pages[i].SpineIndex < spine) { best = i; continue; }
            if (Pages[i].SpineIndex > spine) return best;
            best = i;
            if (offset <= Pages[i].EndOffset) return i;
        }
        return Pages.Count == 0 ? 0 : Math.Clamp(best, 0, Pages.Count - 1);
    }

    public void Dispose() => Book.Dispose();
}
