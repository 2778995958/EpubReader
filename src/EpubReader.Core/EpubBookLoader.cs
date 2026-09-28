using VersOne.Epub;
using VersOne.Epub.Schema;

namespace EpubReader.Core;

public static class EpubBookLoader
{
    public static Book Open(string path, int imageThreshold)
    {
        var stream = SharedFile.ReadAll(path);
        VersOne.Epub.EpubBookRef epub;
        try { epub = VersOne.Epub.EpubReader.OpenBook(stream); }
        catch
        {
            stream.Dispose();
            throw;
        }
        var lifetime = new SharedFile.Holding(epub, stream);
        var language = epub.Schema.Package.Metadata.Languages.FirstOrDefault()?.Language ?? "ja";
        var font = PickFont(language);
        var progression = epub.Schema.Package.Spine.PageProgressionDirection == EpubPageProgressionDirection.RIGHT_TO_LEFT
            ? PageProgression.Rtl
            : PageProgression.Ltr;

        var cssMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var css in epub.Content.Css.Local)
        {
            try { cssMap[css.Key] = css.ReadContent(); }
            catch { /* ignore broken stylesheet */ }
        }

        byte[]? Load(string key)
        {
            if (epub.Content.AllFiles.TryGetLocalFileRefByFilePath(key, out var byPath) && byPath != null)
                return ReadBytes(byPath);
            if (epub.Content.AllFiles.TryGetLocalFileRefByKey(key, out var byKey) && byKey != null)
                return ReadBytes(byKey);
            if (epub.Content.AllFiles.Local.FirstOrDefault(f => f.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) is { } hit)
                return ReadBytes(hit);
            var name = key.Split('/').Last();
            var loose = epub.Content.AllFiles.Local.FirstOrDefault(f => f.Key.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase) || f.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
            return loose == null ? null : ReadBytes(loose);
        }

        if (IsImageSpine(epub))
            return OpenImageSpineOrDispose(epub, path, imageThreshold, Load, language, progression, lifetime);

        var illustrations = new List<Illustration>();
        var spine = new List<SpineItem>();
        List<EpubLocalTextContentFileRef> readingOrder;
        try { readingOrder = epub.GetReadingOrder(); }
        catch { readingOrder = []; }
        if (readingOrder.Count == 0)
            return OpenImageSpineOrDispose(epub, path, imageThreshold, Load, language, progression, lifetime);
        var seenImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < readingOrder.Count; i++)
        {
            var item = readingOrder[i];
            string html;
            try { html = item.ReadContent(); }
            catch { continue; }

            var sheet = MergeCss(cssMap, html, item.Key);
            var css = CssSubset.Parse(sheet);
            var conv = new XhtmlConverter(css, Load, imageThreshold, item.Key, i, font, illustrations);
            var blocks = conv.Convert(html);

            foreach (var ill in illustrations)
            {
                if (ill.SpineIndex == i) seenImages.Add(ill.ResourceKey);
            }

            var visible = blocks.Where(b => b is not PageBreakBlock && !IsBlankParagraph(b)).ToList();
            var onlyImage = visible.Count == 1 && visible[0] is IllustrationBlock ib ? ib : null;

            var spread = ReadSpread(epub, item.Key);
            if (onlyImage != null)
            {
                spine.Add(new SpineItem
                {
                    Index = i,
                    Key = item.Key,
                    Title = System.IO.Path.GetFileNameWithoutExtension(item.Key),
                    Kind = SpineKind.Image,
                    Spread = spread,
                    ImageKey = onlyImage.ResourceKey,
                    ImageWidth = onlyImage.PixelWidth,
                    ImageHeight = onlyImage.PixelHeight
                });
            }
            else
            {
                spine.Add(new SpineItem
                {
                    Index = i,
                    Key = item.Key,
                    Title = FirstHeading(blocks) ?? System.IO.Path.GetFileNameWithoutExtension(item.Key),
                    Kind = SpineKind.Reflowable,
                    Spread = spread,
                    Blocks = blocks
                });
            }
        }

        ScanManifestImages(epub, Load, imageThreshold, illustrations, seenImages, spine);

        var nav = BuildToc(epub, spine);
        int imageSpines = spine.Count(s => s.Kind == SpineKind.Image);
        int textSpines = spine.Count(s => s.Kind == SpineKind.Reflowable);
        // 幾乎全是圖才當漫畫；輕小說口繪不算。漫畫≠EPUB 固定版面。
        var manga = imageSpines > 0 && textSpines == 0;
        var fixedLayout = DetectFixedLayout(epub);
        var (hasSpreads, pairLtr) = DetectSpreadStyle(epub);
        var writing = DetectWriting(progression, fixedLayout, pairLtr, hasSpreads && !pairLtr);

        return new Book
        {
            FilePath = path,
            Title = string.IsNullOrWhiteSpace(epub.Title) ? System.IO.Path.GetFileNameWithoutExtension(path) : epub.Title,
            Author = epub.Author ?? "",
            Language = language,
            DefaultWritingMode = writing,
            Progression = progression,
            IsManga = manga,
            IsFixedLayout = fixedLayout,
            HasPageSpreads = hasSpreads,
            PairLeftToRight = pairLtr,
            Spine = spine,
            Toc = nav,
            Illustrations = illustrations
                .GroupBy(x => x.ResourceKey, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList(),
            LoadResource = Load,
            Lifetime = lifetime,
            ImageThreshold = imageThreshold
        };
    }

    static PageSpreadKind SpreadKindOf(EpubSpineItemRef item)
    {
        if (item.Properties == null) return PageSpreadKind.None;
        foreach (var p in item.Properties)
        {
            if (p == EpubSpineProperty.PAGE_SPREAD_LEFT) return PageSpreadKind.Left;
            if (p == EpubSpineProperty.PAGE_SPREAD_RIGHT) return PageSpreadKind.Right;
            if (p == EpubSpineProperty.PAGE_SPREAD_CENTER) return PageSpreadKind.Center;
            var name = p.ToString();
            if (name.Contains("SPREAD_LEFT", StringComparison.OrdinalIgnoreCase)
                || name.Contains("page-spread-left", StringComparison.OrdinalIgnoreCase))
                return PageSpreadKind.Left;
            if (name.Contains("SPREAD_RIGHT", StringComparison.OrdinalIgnoreCase)
                || name.Contains("page-spread-right", StringComparison.OrdinalIgnoreCase))
                return PageSpreadKind.Right;
            if (name.Contains("SPREAD_CENTER", StringComparison.OrdinalIgnoreCase)
                || name.Contains("page-spread-center", StringComparison.OrdinalIgnoreCase))
                return PageSpreadKind.Center;
        }
        return PageSpreadKind.None;
    }

    static PageSpreadKind ReadSpread(EpubBookRef epub, string itemKey)
    {
        var key = itemKey.Replace('\\', '/');
        var name = key.Split('/').Last();
        var man = epub.Schema.Package.Manifest.Items.FirstOrDefault(m =>
        {
            var href = (m.Href ?? "").Replace('\\', '/');
            return href.Equals(key, StringComparison.OrdinalIgnoreCase)
                   || href.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase)
                   || key.EndsWith("/" + href, StringComparison.OrdinalIgnoreCase);
        });
        if (man == null) return PageSpreadKind.None;
        var sp = epub.Schema.Package.Spine.Items.FirstOrDefault(s =>
            s.IdRef.Equals(man.Id, StringComparison.OrdinalIgnoreCase));
        return sp == null ? PageSpreadKind.None : SpreadKindOf(sp);
    }

    static (bool HasSpreads, bool PairLeftToRight) DetectSpreadStyle(EpubBookRef epub)
    {
        int lr = 0, rl = 0;
        var items = epub.Schema.Package.Spine.Items;
        for (int i = 0; i + 1 < items.Count; i++)
        {
            var a = SpreadKindOf(items[i]);
            var b = SpreadKindOf(items[i + 1]);
            if (a == PageSpreadKind.Right && b == PageSpreadKind.Left) rl++;
            if (a == PageSpreadKind.Left && b == PageSpreadKind.Right) lr++;
        }
        return (lr + rl > 0, lr > rl);
    }

    static bool DetectFixedLayout(EpubBookRef epub)
    {
        bool sawReflowable = false;
        foreach (var m in epub.Schema.Package.Metadata.MetaItems)
        {
            var prop = (m.Property ?? m.Name ?? "");
            var val = m.Content ?? "";
            if (!prop.Contains("rendition:layout", StringComparison.OrdinalIgnoreCase))
                continue;
            if (val.Contains("pre-paginated", StringComparison.OrdinalIgnoreCase))
                return true;
            if (val.Contains("reflowable", StringComparison.OrdinalIgnoreCase))
                sawReflowable = true;
        }
        if (sawReflowable) return false;
        var items = epub.Schema.Package.Spine.Items;
        if (items.Count == 0) return false;
        int pre = 0;
        foreach (var item in items)
        {
            if (item.Properties == null) continue;
            foreach (var p in item.Properties)
            {
                if (p == EpubSpineProperty.LAYOUT_PRE_PAGINATED)
                {
                    pre++;
                    break;
                }
            }
        }
        return pre * 2 >= items.Count;
    }

    static bool IsImageSpine(EpubBookRef epub)
    {
        var spine = epub.Schema.Package.Spine.Items;
        var manifest = epub.Schema.Package.Manifest.Items;
        if (spine.Count == 0) return false;
        int images = 0;
        foreach (var item in spine)
        {
            var man = manifest.FirstOrDefault(m => m.Id.Equals(item.IdRef, StringComparison.OrdinalIgnoreCase));
            if (man != null && IsImageManifest(man))
                images++;
        }
        return images * 2 >= spine.Count;
    }

    static bool IsImageManifest(EpubManifestItem man)
    {
        var media = man.MediaType ?? "";
        if (media.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return true;
        var href = man.Href ?? "";
        return ImageSizeReader.IsImageExtension(Path.GetExtension(href));
    }

    static Book OpenImageSpineOrDispose(
        EpubBookRef epub,
        string path,
        int imageThreshold,
        Func<string, byte[]?> load,
        string language,
        PageProgression progression,
        IDisposable lifetime)
    {
        try { return OpenImageSpine(epub, path, imageThreshold, load, language, progression, lifetime); }
        catch
        {
            lifetime.Dispose();
            throw;
        }
    }

    static Book OpenImageSpine(
        EpubBookRef epub,
        string path,
        int imageThreshold,
        Func<string, byte[]?> load,
        string language,
        PageProgression progression,
        IDisposable lifetime)
    {
        var illustrations = new List<Illustration>();
        var spine = new List<SpineItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifest = epub.Schema.Package.Manifest.Items;

        foreach (var item in epub.Schema.Package.Spine.Items)
        {
            var man = manifest.FirstOrDefault(m => m.Id.Equals(item.IdRef, StringComparison.OrdinalIgnoreCase));
            if (man == null || !IsImageManifest(man)) continue;
            var href = (man.Href ?? "").Replace('\\', '/');
            if (href.Contains("publicimages/", StringComparison.OrdinalIgnoreCase)) continue;
            var data = load(href);
            if (data == null) continue;
            var size = ImageSizeReader.TryRead(data);
            if (size == null) continue;
            if (Math.Max(size.Value.Width, size.Value.Height) < imageThreshold) continue;
            var key = ResolveImageKey(epub, href);
            if (!seen.Add(key)) continue;
            int index = spine.Count;
            spine.Add(new SpineItem
            {
                Index = index,
                Key = key,
                Title = Path.GetFileNameWithoutExtension(href),
                Kind = SpineKind.Image,
                Spread = SpreadKindOf(item),
                ImageKey = key,
                ImageWidth = size.Value.Width,
                ImageHeight = size.Value.Height
            });
            illustrations.Add(new Illustration
            {
                ResourceKey = key,
                Label = Path.GetFileName(href),
                SpineIndex = index,
                Width = size.Value.Width,
                Height = size.Value.Height
            });
        }

        if (spine.Count == 0)
            throw new InvalidOperationException("這本 EPUB 的目錄是圖片頁，但找不到可顯示的圖。");

        var toc = BuildToc(epub, spine);
        var fixedLayout = DetectFixedLayout(epub);
        var (hasSpreads, pairLtr) = DetectSpreadStyle(epub);
        return new Book
        {
            FilePath = path,
            Title = string.IsNullOrWhiteSpace(epub.Title) ? Path.GetFileNameWithoutExtension(path) : epub.Title,
            Author = epub.Author ?? "",
            Language = language,
            DefaultWritingMode = DetectWriting(progression, fixedLayout, pairLtr, hasSpreads && !pairLtr),
            Progression = progression,
            IsManga = true,
            IsFixedLayout = fixedLayout,
            HasPageSpreads = true,
            PairLeftToRight = pairLtr,
            Spine = spine,
            Toc = toc,
            Illustrations = illustrations,
            LoadResource = load,
            Lifetime = lifetime,
            ImageThreshold = imageThreshold
        };
    }

    static string ResolveImageKey(EpubBookRef epub, string href)
    {
        if (epub.Content.Images.TryGetLocalFileRefByFilePath(href, out var byPath) && byPath != null)
            return byPath.Key;
        if (epub.Content.Images.TryGetLocalFileRefByKey(href, out var byKey) && byKey != null)
            return byKey.Key;
        var name = href.Split('/').Last();
        var loose = epub.Content.Images.Local.FirstOrDefault(f =>
            f.Key.Equals(href, StringComparison.OrdinalIgnoreCase)
            || f.Key.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase)
            || f.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
        return loose?.Key ?? href;
    }

    static void ScanManifestImages(
        EpubBookRef epub,
        Func<string, byte[]?> load,
        int threshold,
        List<Illustration> illustrations,
        HashSet<string> seen,
        List<SpineItem> spine)
    {
        foreach (var img in epub.Content.Images.Local)
        {
            if (seen.Contains(img.Key)) continue;
            byte[]? data;
            try { data = img.ReadContent(); }
            catch { continue; }
            var size = ImageSizeReader.TryRead(data);
            if (size == null) continue;
            if (Math.Max(size.Value.Width, size.Value.Height) < threshold) continue;
            int spineIndex = 0;
            for (int i = 0; i < spine.Count; i++)
            {
                if (spine[i].ImageKey == img.Key
                    || spine[i].Blocks.OfType<IllustrationBlock>().Any(b => b.ResourceKey == img.Key))
                {
                    spineIndex = i;
                    break;
                }
            }
            illustrations.Add(new Illustration
            {
                ResourceKey = img.Key,
                Label = System.IO.Path.GetFileName(img.Key),
                SpineIndex = spineIndex,
                Width = size.Value.Width,
                Height = size.Value.Height
            });
        }
    }

    static List<TocItem> BuildToc(EpubBookRef epub, List<SpineItem> spine)
    {
        try
        {
            var nav = epub.GetNavigation();
            if (nav is { Count: > 0 })
                return MapNav(nav, spine);
        }
        catch { /* fall back */ }

        return spine.Select(s => new TocItem { Title = s.Title, SpineIndex = s.Index }).ToList();
    }

    static List<TocItem> MapNav(IList<EpubNavigationItemRef> items, List<SpineItem> spine)
    {
        var list = new List<TocItem>();
        foreach (var item in items)
        {
            int idx = 0;
            var href = item.Link?.ContentFileUrl ?? item.HtmlContentFileRef?.Key;
            if (!string.IsNullOrEmpty(href))
            {
                var key = href.Replace('\\', '/');
                idx = spine.FindIndex(s =>
                    s.Key.Equals(key, StringComparison.OrdinalIgnoreCase)
                    || s.Key.EndsWith("/" + key, StringComparison.OrdinalIgnoreCase)
                    || key.EndsWith("/" + s.Key, StringComparison.OrdinalIgnoreCase));
                if (idx < 0) idx = 0;
            }
            list.Add(new TocItem
            {
                Title = string.IsNullOrWhiteSpace(item.Title) ? spine[Math.Clamp(idx, 0, Math.Max(0, spine.Count - 1))].Title : item.Title,
                SpineIndex = idx,
                Children = item.NestedItems is { Count: > 0 } ? MapNav(item.NestedItems, spine) : []
            });
        }
        return list;
    }

    static string MergeCss(Dictionary<string, string> cssMap, string html, string chapterKey)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kv in cssMap) sb.Append(kv.Value).Append('\n');
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     html, @"<link[^>]+href\s*=\s*[""']([^""']+)[""'][^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            var href = Href.Combine(chapterKey, m.Groups[1].Value);
            if (cssMap.TryGetValue(href, out var c)) continue;
            var name = href.Split('/').Last();
            var hit = cssMap.FirstOrDefault(kv => kv.Key.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase));
            if (hit.Value != null) sb.Append(hit.Value).Append('\n');
        }
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     html, @"<style[^>]*>(.*?)</style>", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline))
            sb.Append(m.Groups[1].Value).Append('\n');
        return sb.ToString();
    }

    static byte[]? ReadBytes(EpubLocalContentFileRef file)
    {
        try { return file.ReadContentAsBytes(); }
        catch { return null; }
    }

    static string PickFont(string language) => CjkFonts.DefaultFamily(language);

    static WritingMode DetectWriting(PageProgression ppd, bool fixedLayout, bool pairLeftToRight, bool hasRightLeft)
    {
        // 左右成對（寫真集）＝橫排。右左成對（漫畫）即使 pre-paginated 也跟 spine：右左＝直排。
        if (pairLeftToRight) return WritingMode.Horizontal;
        if (hasRightLeft) return ppd == PageProgression.Rtl ? WritingMode.Vertical : WritingMode.Horizontal;
        if (fixedLayout) return WritingMode.Horizontal;
        return ppd == PageProgression.Rtl ? WritingMode.Vertical : WritingMode.Horizontal;
    }

    static bool IsBlankParagraph(BlockNode block)
    {
        if (block is not ParagraphBlock p) return false;
        foreach (var n in p.Inlines)
        {
            switch (n)
            {
                case TextRun t when !string.IsNullOrWhiteSpace(t.Text):
                case RubyInline:
                case TateChuYoko:
                case InlineImage:
                    return false;
            }
        }
        return true;
    }

    static string? FirstHeading(List<BlockNode> blocks) =>
        blocks.OfType<ParagraphBlock>().FirstOrDefault(p => p.IsHeading) is { } h
            ? string.Concat(h.Inlines.OfType<TextRun>().Select(t => t.Text)).Trim()
            : null;
}
