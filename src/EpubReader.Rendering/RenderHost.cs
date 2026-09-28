using System.Numerics;
using EpubReader.Core;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Vortice.WIC;
using D2D = Vortice.Direct2D1.D2D1;
using DW = Vortice.DirectWrite.DWrite;
using DxgiFormat = Vortice.DXGI.Format;
using WicPixelFormat = Vortice.WIC.PixelFormat;

namespace EpubReader.Rendering;

public sealed class RenderHost : ITextMeasurer
{
    readonly IDWriteFactory2 _dwrite;
    readonly ID2D1Factory _d2d;
    readonly IWICImagingFactory _wic;
    readonly CjkFontFallback _fonts;
    readonly Dictionary<string, IDWriteTextFormat> _formats = [];
    readonly Dictionary<string, (int W, int H, byte[] Bgra)> _imageCache = [];

    public string FontFamily => _fonts.Family;

    public RenderHost(string? preferredFamily = null, string? language = null)
    {
        _dwrite = DW.DWriteCreateFactory<IDWriteFactory2>();
        _d2d = D2D.D2D1CreateFactory<ID2D1Factory>(Vortice.Direct2D1.FactoryType.SingleThreaded);
        _wic = new IWICImagingFactory();
        _fonts = new CjkFontFallback(_dwrite, preferredFamily, language);
    }

    public MeasuredSize Measure(string text, TextStyle style, WritingMode mode)
    {
        if (string.IsNullOrEmpty(text))
            return new MeasuredSize(0, style.FontSizePx, 0);
        LayoutBox(mode, style.FontSizePx, drawing: false, out float maxW, out float maxH);
        using var layout = CreateLayout(text, style, mode, maxW, maxH);
        var m = layout.Metrics;
        float w = Math.Max(m.WidthIncludingTrailingWhitespace, m.Width);
        float h = m.Height;
        if (h < 0.1f) h = style.FontSizePx;
        if (w < 0.1f) w = style.FontSizePx * (mode == WritingMode.Vertical ? 1 : 0.5f);
        if (mode == WritingMode.Vertical)
            w = Math.Min(w, Math.Max(style.FontSizePx, style.FontSizePx * 1.35f));
        float adv = mode == WritingMode.Vertical ? h : w;
        return new MeasuredSize(w, h, adv);
    }

    public RenderedPage Render(IReadOnlyList<PageModel> pages, LayoutSettings settings, Func<string, byte[]?> load, float dpiScale)
    {
        int w = Math.Max(1, (int)Math.Ceiling(settings.PageWidth * dpiScale));
        int h = Math.Max(1, (int)Math.Ceiling(settings.PageHeight * dpiScale));
        using var wicBmp = _wic.CreateBitmap((uint)w, (uint)h, WicPixelFormat.Format32bppPBGRA, BitmapCreateCacheOption.CacheOnLoad);
        var rtProps = new RenderTargetProperties
        {
            Type = RenderTargetType.Default,
            PixelFormat = new Vortice.DCommon.PixelFormat(DxgiFormat.B8G8R8A8_UNorm, AlphaMode.Premultiplied),
            DpiX = 96,
            DpiY = 96,
            Usage = RenderTargetUsage.None,
            MinLevel = FeatureLevel.Default
        };
        using var rt = _d2d.CreateWicBitmapRenderTarget(wicBmp, rtProps);
        rt.BeginDraw();
        if (dpiScale > 1.01f)
            rt.Transform = Matrix3x2.CreateScale(dpiScale, dpiScale);
        rt.PushAxisAlignedClip(new Rect(0, 0, settings.PageWidth, settings.PageHeight), AntialiasMode.Aliased);
        rt.Clear(ToColor(settings.PaperArgb));
        using var ink = rt.CreateSolidColorBrush(ToColor(settings.InkArgb));
        using var dim = rt.CreateSolidColorBrush(ToColor(Dim(settings.InkArgb)));

        if (pages.Count == 2)
        {
            const float overlap = 0f;
            float half = settings.PageWidth / 2f;
            float slot = half + overlap * 0.5f;
            // 橫排／左至右：pages[0] 在左；直排：pages[0] 在右。
            bool ltr = settings.VisualLtr;
            var left = ltr ? pages[0] : pages[1];
            var right = ltr ? pages[1] : pages[0];
            DrawSpreadSlot(rt, left, settings, load, ink, dim, 0, slot, gutterRight: true);
            DrawSpreadSlot(rt, right, settings, load, ink, dim, half - overlap * 0.5f, slot, gutterRight: false);
        }
        else if (pages.Count == 1)
        {
            DrawPage(rt, pages[0], settings, load, ink, dim, 0);
        }

        rt.PopAxisAlignedClip();
        rt.EndDraw();
        int stride = w * 4;
        var bgra = new byte[stride * h];
        wicBmp.CopyPixels((uint)stride, bgra);
        return new RenderedPage(w, h, bgra);
    }

    void DrawSpreadSlot(ID2D1RenderTarget rt, PageModel page, LayoutSettings settings, Func<string, byte[]?> load, ID2D1SolidColorBrush ink, ID2D1SolidColorBrush dim, float x, float slotW, bool gutterRight)
    {
        var img = page.Ops.OfType<DrawImageOp>().FirstOrDefault();
        if (img != null)
        {
            var decoded = GetImage(img.ResourceKey, load);
            float srcW = decoded?.W ?? Math.Max(1, img.Width);
            float srcH = decoded?.H ?? Math.Max(1, img.Height);
            float boxW = Math.Max(8, slotW);
            float boxH = Math.Max(8, settings.PageHeight);
            float scale = Math.Min(boxW / srcW, boxH / srcH);
            float dw = srcW * scale;
            float dh = srcH * scale;
            var fitted = img with
            {
                X = gutterRight ? x + slotW - dw : x,
                Y = (settings.PageHeight - dh) / 2,
                Width = dw,
                Height = dh
            };
            DrawImage(rt, fitted, load, 0);
            return;
        }
        DrawPage(rt, page, settings, load, ink, dim, x);
    }

    void DrawPage(ID2D1RenderTarget rt, PageModel page, LayoutSettings settings, Func<string, byte[]?> load, ID2D1SolidColorBrush ink, ID2D1SolidColorBrush dim, float xOffset)
    {
        foreach (var op in page.Ops)
        {
            switch (op)
            {
                case DrawImageOp img:
                    DrawImage(rt, img, load, xOffset);
                    break;
                case DrawTextOp t:
                    DrawText(rt, t, ink, dim, xOffset);
                    break;
                case DrawRubyOp r:
                    DrawRuby(rt, r, ink, dim, xOffset);
                    break;
            }
        }
    }

    void DrawText(ID2D1RenderTarget rt, DrawTextOp t, ID2D1SolidColorBrush ink, ID2D1SolidColorBrush dim, float xOffset)
    {
        var mode = t.UprightCombine ? WritingMode.Horizontal : t.Mode;
        LayoutBox(mode, t.Style.FontSizePx, drawing: true, out float maxW, out float maxH);
        var style = t.Style;
        var layout = CreateLayout(t.Text, style, mode, maxW, maxH);
        var m = layout.Metrics;
        if (t.UprightCombine)
        {
            float fit = Math.Min(t.Width / Math.Max(m.Width, 0.01f), t.Height / Math.Max(m.Height, 0.01f));
            if (fit < 0.98f)
            {
                layout.Dispose();
                style = t.Style.WithSize(Math.Max(6f, t.Style.FontSizePx * fit * 0.92f));
                layout = CreateLayout(t.Text, style, mode, maxW, maxH);
                m = layout.Metrics;
            }
        }
        try
        {
            float ox = t.X + xOffset;
            float oy = t.Y;
            if (t.UprightCombine)
            {
                ox += Math.Max(0, (t.Width - m.Width) / 2);
                oy += Math.Max(0, (t.Height - m.Height) / 2);
            }
            // FlowDirection.RightToLeft 時墨在盒右側；扣 Left/Top 才對得上排版座標。
            var origin = new Vector2(ox - m.Left, oy - m.Top);
            using var brush = rt.CreateSolidColorBrush(ToColor(t.Style.ColorArgb == 0 ? 0xFF2A2118 : t.Style.ColorArgb));
            rt.DrawTextLayout(origin, layout, brush, DrawTextOptions.EnableColorFont);
            DrawEmphasis(rt, t, dim, xOffset);
        }
        finally
        {
            layout.Dispose();
        }
    }

    void DrawRuby(ID2D1RenderTarget rt, DrawRubyOp r, ID2D1SolidColorBrush ink, ID2D1SolidColorBrush dim, float xOffset)
    {
        LayoutBox(r.Mode, r.Style.FontSizePx, drawing: true, out float maxW, out float maxH);
        using var baseLayout = CreateLayout(r.BaseText, r.Style, r.Mode, maxW, maxH);
        var rubyStyle = r.Style.WithSize(r.Style.FontSizePx * 0.5f);
        using var rubyLayout = CreateLayout(r.Annotation, rubyStyle, r.Mode, maxW, maxH);
        using var brush = rt.CreateSolidColorBrush(ToColor(r.Style.ColorArgb == 0 ? 0xFF2A2118 : r.Style.ColorArgb));
        using var rubyBrush = rt.CreateSolidColorBrush(ToColor(Dim(r.Style.ColorArgb == 0 ? 0xFF2A2118 : r.Style.ColorArgb)));
        var bm = baseLayout.Metrics;
        var rm = rubyLayout.Metrics;
        if (r.Mode == WritingMode.Horizontal)
        {
            float rubyW = rm.Width;
            float baseW = bm.Width;
            rt.DrawTextLayout(
                new Vector2(r.X + xOffset + Math.Max(0, (r.Width - rubyW) / 2) - rm.Left, r.Y - rm.Top),
                rubyLayout, rubyBrush, DrawTextOptions.EnableColorFont);
            rt.DrawTextLayout(
                new Vector2(r.X + xOffset + Math.Max(0, (r.Width - baseW) / 2) - bm.Left, r.Y + rm.Height * 0.85f - bm.Top),
                baseLayout, brush, DrawTextOptions.EnableColorFont);
        }
        else
        {
            float rubyH = rm.Height;
            float baseH = Math.Max(bm.Height, 0.1f);
            // 本字貼在格子上沿；注音在右側行間、對本字置中，溢出也不拉本字。
            float cell = Math.Max(r.Width, r.Style.FontSizePx);
            float rubyX = r.X + xOffset + cell;
            float baseY = r.Y - bm.Top;
            float rubyY = r.Y + (baseH - rubyH) / 2f - rm.Top;
            rt.DrawTextLayout(
                new Vector2(rubyX - rm.Left, rubyY),
                rubyLayout, rubyBrush, DrawTextOptions.EnableColorFont);
            rt.DrawTextLayout(
                new Vector2(r.X + xOffset - bm.Left, baseY),
                baseLayout, brush, DrawTextOptions.EnableColorFont);
        }
    }

    void DrawEmphasis(ID2D1RenderTarget rt, DrawTextOp t, ID2D1SolidColorBrush dim, float xOffset)
    {
        if (t.Style.Emphasis == EmphasisKind.None) return;
        float rad = Math.Max(1.2f, t.Style.FontSizePx * 0.08f);
        float cx, cy;
        if (t.Mode == WritingMode.Horizontal)
        {
            cx = t.X + xOffset + t.Width / 2;
            cy = t.Y - rad * 1.5f;
        }
        else
        {
            cx = t.X + xOffset + t.Width + rad * 1.2f;
            cy = t.Y + t.Height / 2;
        }
        rt.FillEllipse(new Ellipse(new Vector2(cx, cy), rad, t.Style.Emphasis == EmphasisKind.Sesame ? rad * 1.6f : rad), dim);
    }

    void DrawImage(ID2D1RenderTarget rt, DrawImageOp img, Func<string, byte[]?> load, float xOffset)
    {
        var decoded = GetImage(img.ResourceKey, load);
        if (decoded == null) return;
        var (iw, ih, bgra) = decoded.Value;
        var props = new BitmapProperties(new Vortice.DCommon.PixelFormat(DxgiFormat.B8G8R8A8_UNorm, AlphaMode.Premultiplied), 96, 96);
        unsafe
        {
            fixed (byte* p = bgra)
            {
                using var bmp = rt.CreateBitmap(new SizeI(iw, ih), (IntPtr)p, (uint)(iw * 4), props);
                var dest = new Rect(img.X + xOffset, img.Y, img.Width, img.Height);
                var src = new Rect(0, 0, iw, ih);
                rt.DrawBitmap(bmp, dest, 1f, Vortice.Direct2D1.BitmapInterpolationMode.Linear, src);
            }
        }
    }

    (int W, int H, byte[] Bgra)? GetImage(string key, Func<string, byte[]?> load)
    {
        if (_imageCache.TryGetValue(key, out var hit)) return hit;
        var bytes = load(key);
        if (bytes == null) return null;
        try
        {
            using var stream = _wic.CreateStream(new MemoryStream(bytes, writable: false));
            using var decoder = _wic.CreateDecoderFromStream(stream, DecodeOptions.CacheOnDemand);
            using var frame = decoder.GetFrame(0);
            using var conv = _wic.CreateFormatConverter();
            conv.Initialize(frame, WicPixelFormat.Format32bppPBGRA);
            int iw = (int)conv.Size.Width;
            int ih = (int)conv.Size.Height;
            if (iw <= 0 || ih <= 0) return null;
            var bgra = new byte[iw * ih * 4];
            conv.CopyPixels((uint)(iw * 4), bgra);
            var entry = (iw, ih, bgra);
            if (_imageCache.Count > 64)
                _imageCache.Clear();
            _imageCache[key] = entry;
            return entry;
        }
        catch
        {
            return null;
        }
    }

    const float LayoutLong = 4096f;

    static void LayoutBox(WritingMode mode, float em, bool drawing, out float maxW, out float maxH)
    {
        em = Math.Max(8f, em);
        float cross = drawing ? em * 4f : em * 1.6f;
        if (mode == WritingMode.Vertical)
        {
            maxW = cross;
            maxH = LayoutLong;
        }
        else
        {
            maxW = LayoutLong;
            maxH = cross;
        }
    }

    IDWriteTextLayout CreateLayout(string text, TextStyle style, WritingMode mode, float maxW, float maxH)
    {
        var format = GetFormat(style, mode);
        var layout = _dwrite.CreateTextLayout(text, format, Math.Max(maxW, 1), Math.Max(maxH, 1));
        if (mode == WritingMode.Vertical)
        {
            try
            {
                using var typo = _dwrite.CreateTypography();
                // 只用 vert。vrt2 會再轉一次拉丁字母，英文會倒過來。
                typo.AddFontFeature(new FontFeature { NameTag = FontFeatureTag.VerticalWriting, Parameter = 1 });
                layout.SetTypography(typo, new TextRange(0, (uint)text.Length));
            }
            catch
            {
                /* ReadingDirection 已是直排；typography 失敗也不擋繪製 */
            }
        }
        return layout;
    }

    IDWriteTextFormat GetFormat(TextStyle style, WritingMode mode)
    {
        var family = _fonts.ResolveFamily(style.FontFamily);
        string key = $"{family}|{style.FontSizePx:0.##}|{style.Bold}|{style.Italic}|{mode}";
        if (_formats.TryGetValue(key, out var cached)) return cached;
        var format = _dwrite.CreateTextFormat(
            family,
            null,
            style.Bold ? FontWeight.Bold : FontWeight.Regular,
            style.Italic ? FontStyle.Italic : FontStyle.Normal,
            FontStretch.Normal,
            Math.Max(6, style.FontSizePx),
            _fonts.Locale);
        format.WordWrapping = WordWrapping.NoWrap;
        _fonts.Apply(format);
        if (mode == WritingMode.Vertical)
        {
            format.ReadingDirection = ReadingDirection.TopToBottom;
            format.FlowDirection = FlowDirection.RightToLeft;
            if (format is IDWriteTextFormat1 f1)
                f1.VerticalGlyphOrientation = VerticalGlyphOrientation.Default;
        }
        else
        {
            format.ReadingDirection = ReadingDirection.LeftToRight;
            format.FlowDirection = FlowDirection.TopToBottom;
        }
        _formats[key] = format;
        return format;
    }

    static Color4 ToColor(uint argb)
    {
        float a = ((argb >> 24) & 0xFF) / 255f;
        float r = ((argb >> 16) & 0xFF) / 255f;
        float g = ((argb >> 8) & 0xFF) / 255f;
        float b = (argb & 0xFF) / 255f;
        return new Color4(r, g, b, a);
    }

    static uint Dim(uint argb)
    {
        uint a = (argb >> 24) & 0xFF;
        uint r = (uint)(((argb >> 16) & 0xFF) * 0.55);
        uint g = (uint)(((argb >> 8) & 0xFF) * 0.55);
        uint b = (uint)((argb & 0xFF) * 0.55);
        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    public void Dispose()
    {
        foreach (var f in _formats.Values) f.Dispose();
        _formats.Clear();
        _fonts.Dispose();
        _wic.Dispose();
        _d2d.Dispose();
        _dwrite.Dispose();
        GC.SuppressFinalize(this);
    }
}

public readonly record struct RenderedPage(int Width, int Height, byte[] Bgra);
