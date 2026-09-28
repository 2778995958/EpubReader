namespace EpubReader.Core;

public static class LayoutEngine
{
    // 行頭禁則／行尾禁則（含 VerticalPunct 直式形）。
    const string CannotStart =
        "、。，．・？！：；゛゜ヽヾゝゞ々ー〻）」』】〕］｝〉》〟)]!?.,;:" +
        "︒︑︕︖︓︔︶﹂﹄︼︺﹈︸﹀︾︱︲";
    const string CannotEnd =
        "「『【〔［｛〈《〝([{﹁﹃︻︵﹇︷︿︽︹";

    public static List<PageModel> Layout(Book book, LayoutSettings settings, ITextMeasurer measurer)
    {
        var pages = new List<PageModel>();
        foreach (var item in book.Spine)
        {
            if (item.Kind == SpineKind.Image && item.ImageKey != null)
                pages.Add(ImagePage(pages.Count, item, settings));
            else
                LayoutChapter(item, settings, measurer, pages);
        }
        if (pages.Count == 0)
            pages.Add(new PageModel { Index = 0, SpineIndex = 0 });
        for (int i = 0; i < pages.Count; i++)
            pages[i].Index = i;
        return pages;
    }

    static PageModel ImagePage(int index, SpineItem item, LayoutSettings s)
    {
        var page = new PageModel
        {
            Index = index,
            SpineIndex = item.Index,
            IsImagePage = true,
            Spread = item.Spread
        };
        page.Ops.Add(FitImage(item.ImageKey!, item.ImageWidth, item.ImageHeight, s.PageWidth, s.PageHeight, 0, 0));
        return page;
    }

    static DrawImageOp FitImage(string key, int pixelW, int pixelH, float boxW, float boxH, float originX, float originY)
    {
        float iw = Math.Max(1, pixelW);
        float ih = Math.Max(1, pixelH);
        float scale = Math.Min(boxW / iw, boxH / ih);
        float w = iw * scale;
        float h = ih * scale;
        return new DrawImageOp(key, originX + (boxW - w) / 2, originY + (boxH - h) / 2, w, h);
    }

    static void LayoutChapter(SpineItem item, LayoutSettings s, ITextMeasurer measurer, List<PageModel> pages)
    {
        var page = NewPage(pages.Count, item.Index, 0, item.Spread);
        float margin = s.Margin;
        float maxW = s.PageWidth - margin * 2;
        float maxH = s.PageHeight - margin * 2;
        bool vertical = s.WritingMode == WritingMode.Vertical;
        float cursorCross = 0; // y for horizontal, x-from-right for vertical
        float cursorMain = 0;  // x for horizontal, y for vertical
        int charOffset = 0;
        float lineThickness = 0;

        float lineEm = 0;

        void FlushLineAdvance()
        {
            if (lineThickness <= 0) return;
            // 直排行送り用字級×行高；注音伸進行間，不把「本字+ruby」再乘一次。
            float pitch = vertical
                ? Math.Max(lineEm * s.LineHeight, lineThickness + lineEm * 0.2f)
                : lineThickness * s.LineHeight;
            cursorCross += pitch;
            cursorMain = 0;
            lineThickness = 0;
            lineEm = 0;
        }

        bool NeedNewPage()
        {
            float used = cursorCross + (vertical
                ? Math.Max(lineEm * s.LineHeight, lineThickness + lineEm * 0.2f)
                : lineThickness * s.LineHeight);
            float limit = vertical ? maxW : maxH;
            return used > limit && page.Ops.Count > 0;
        }

        void NewPageIfNeeded()
        {
            if (!NeedNewPage()) return;
            page.EndOffset = charOffset;
            pages.Add(page);
            page = NewPage(pages.Count, item.Index, charOffset, item.Spread);
            cursorCross = 0;
            cursorMain = 0;
            lineThickness = 0;
            lineEm = 0;
        }

        (float x, float y) Place(float main, float cross, float w, float h, float em)
        {
            if (!vertical)
                return (margin + main, margin + cross);
            // 直排字面用字級對齊，不用量測寬（ruby / 本文才不會各偏一邊）。
            float cell = Math.Max(em > 0.1f ? em : w, 1f);
            float x = s.PageWidth - margin - cross - cell;
            return (x, margin + main);
        }

        foreach (var block in item.Blocks)
        {
            if (block is PageBreakBlock)
            {
                FlushLineAdvance();
                if (page.Ops.Count > 0)
                {
                    page.EndOffset = charOffset;
                    pages.Add(page);
                    page = NewPage(pages.Count, item.Index, charOffset, item.Spread);
                    cursorCross = 0;
                    cursorMain = 0;
                    lineThickness = 0;
                    lineEm = 0;
                }
                continue;
            }

            if (block is IllustrationBlock img)
            {
                if (img.IsOrnament || img.IsFlowStrip)
                {
                    FlushLineAdvance();
                    NewPageIfNeeded();
                    float em = 26f * Math.Max(0.6f, s.FontScale);
                    float boxW, boxH;
                    if (img.IsOrnament)
                    {
                        boxW = Math.Max(0.5f, img.WidthEm) * em;
                        boxH = Math.Max(0.5f, img.HeightEm) * em;
                    }
                    else if (vertical)
                    {
                        boxH = maxH;
                        boxW = boxH * img.PixelWidth / Math.Max(1, img.PixelHeight);
                        boxW = Math.Clamp(boxW, em * 0.8f, em * 3f);
                    }
                    else
                    {
                        boxW = maxW;
                        boxH = boxW * img.PixelHeight / Math.Max(1, img.PixelWidth);
                        boxH = Math.Clamp(boxH, em * 0.8f, em * 3f);
                    }
                    if (vertical)
                    {
                        lineThickness = Math.Max(lineThickness, boxW);
                        lineEm = Math.Max(lineEm, em);
                        float yOff = Math.Max(0, (maxH - boxH) / 2);
                        var (ox, oy) = Place(yOff, cursorCross, boxW, boxH, em);
                        page.Ops.Add(new DrawImageOp(img.ResourceKey, ox, oy, boxW, boxH));
                    }
                    else
                    {
                        lineThickness = Math.Max(lineThickness, boxH);
                        lineEm = Math.Max(lineEm, em);
                        float xOff = Math.Max(0, (maxW - boxW) / 2);
                        var (ox, oy) = Place(xOff, cursorCross, boxW, boxH, em);
                        page.Ops.Add(new DrawImageOp(img.ResourceKey, ox, oy, boxW, boxH));
                    }
                    FlushLineAdvance();
                    NewPageIfNeeded();
                    continue;
                }
                FlushLineAdvance();
                if (HasVisibleOps(page))
                {
                    page.EndOffset = charOffset;
                    pages.Add(page);
                }
                page = NewPage(pages.Count, item.Index, charOffset, item.Spread);
                page.IsImagePage = true;
                page.Ops.Add(FitImage(img.ResourceKey, img.PixelWidth, img.PixelHeight, s.PageWidth, s.PageHeight, 0, 0));
                page.EndOffset = charOffset;
                pages.Add(page);
                page = NewPage(pages.Count, item.Index, charOffset, item.Spread);
                cursorCross = 0;
                cursorMain = 0;
                lineThickness = 0;
                lineEm = 0;
                continue;
            }

            if (block is not ParagraphBlock para) continue;

            float paraEm = (para.Inlines.OfType<TextRun>().FirstOrDefault()?.Style.FontSizePx
                            ?? para.Inlines.OfType<RubyInline>().FirstOrDefault()?.Style.FontSizePx
                            ?? 26) * s.FontScale;
            float spaceBefore = Math.Max(0, para.SpaceBeforeEm) * paraEm;
            float spaceAfter = Math.Max(0, para.SpaceAfterEm) * paraEm;
            // 直排標題另外在行頭（頁面上方）留白，避免頂到工具列。
            float lineStartPad = para.IsHeading
                ? paraEm * (para.HeadingLevel <= 1 ? 0.35f : 0.2f)
                : 0;
            if (spaceBefore > 0 || lineStartPad > 0)
            {
                float blockLimit = vertical ? maxW : maxH;
                float extraCross = spaceBefore;
                if (cursorCross > 0 && cursorCross + extraCross + paraEm * s.LineHeight > blockLimit && page.Ops.Count > 0)
                {
                    page.EndOffset = charOffset;
                    pages.Add(page);
                    page = NewPage(pages.Count, item.Index, charOffset, item.Spread);
                    cursorCross = 0;
                    cursorMain = 0;
                    lineThickness = 0;
                    lineEm = 0;
                }
                cursorCross += extraCross;
            }

            float indent = para.IsHeading ? 0 : para.IndentEm * paraEm;
            float padTop = Math.Max(0, para.PadTopEm) * paraEm;
            if (vertical)
            {
                // padding-top 與 text-indent 都在行頭（畫面上方）；負縮排可掛進 padding，但不能飛出頁面。
                cursorMain = indent + padTop + lineStartPad;
                cursorMain = Math.Max(0, cursorMain);
            }
            else
            {
                cursorMain = Math.Max(0, indent);
                cursorCross += padTop;
            }

            var atoms = Flatten(para, s, measurer);
            for (int ai = 0; ai < atoms.Count; ai++)
            {
                var atom = atoms[ai];
                if (atom.Break)
                {
                    FlushLineAdvance();
                    NewPageIfNeeded();
                    continue;
                }

                float limit = vertical ? maxH : maxW;
                bool wrap = cursorMain > 0 && cursorMain + atom.Advance > limit;
                // 行頭禁則：句點、括號結尾跟前一字，掛在行尾（ぶら下げ）。
                if (wrap && StartsKinsoku(AtomText(atom)))
                    wrap = false;
                // 行尾禁則：開括號不留在行尾，連下一字一起換行。
                if (!wrap && cursorMain > 0 && EndsKinsoku(AtomText(atom)))
                {
                    float need = atom.Advance;
                    if (ai + 1 < atoms.Count && !atoms[ai + 1].Break)
                        need += atoms[ai + 1].Advance;
                    if (cursorMain + need > limit)
                        wrap = true;
                }
                if (wrap)
                {
                    FlushLineAdvance();
                    NewPageIfNeeded();
                }

                lineThickness = Math.Max(lineThickness, atom.Cross);
                lineEm = Math.Max(lineEm, atom.Em);
                var (x, y) = Place(cursorMain, cursorCross, atom.Width, atom.Height, atom.Em);
                page.Ops.Add(atom.ToOp(x, y));
                cursorMain += atom.Advance;
                charOffset += atom.Chars;
            }
            FlushLineAdvance();
            if (spaceAfter > 0)
                cursorCross += spaceAfter;
            NewPageIfNeeded();
        }

        page.EndOffset = charOffset;
        if (HasVisibleOps(page) || pages.Count == 0)
            pages.Add(page);
    }

    static bool HasVisibleOps(PageModel page)
    {
        foreach (var op in page.Ops)
        {
            switch (op)
            {
                case DrawImageOp:
                case DrawRubyOp:
                    return true;
                case DrawTextOp t when !string.IsNullOrWhiteSpace(t.Text):
                    return true;
            }
        }
        return false;
    }

    static List<Atom> Flatten(ParagraphBlock para, LayoutSettings s, ITextMeasurer measurer)
    {
        var list = new List<Atom>();
        foreach (var inline in para.Inlines)
        {
            switch (inline)
            {
                case LineBreakInline:
                    list.Add(Atom.LineBreak());
                    break;
                case TateChuYoko tcy:
                {
                    var style = Scale(tcy.Style, s);
                    float em = style.FontSizePx;
                    if (s.WritingMode == WritingMode.Vertical)
                    {
                        list.Add(new Atom(em, em, em, em, tcy.Text.Length, em,
                            new DrawTextOp(tcy.Text, 0, 0, em, em, style, s.WritingMode, true)));
                    }
                    else
                    {
                        var m = measurer.Measure(tcy.Text, style, WritingMode.Horizontal);
                        float w = Math.Max(em * 0.5f, m.Width);
                        list.Add(new Atom(w, em, w, em, tcy.Text.Length, em,
                            new DrawTextOp(tcy.Text, 0, 0, w, em, style, s.WritingMode, false)));
                    }
                    break;
                }
                case RubyInline ruby:
                {
                    var style = Scale(ruby.Style, s);
                    var baseText = s.WritingMode == WritingMode.Vertical ? VerticalPunct.Apply(ruby.BaseText) : ruby.BaseText;
                    var rubyText = s.WritingMode == WritingMode.Vertical ? VerticalPunct.Apply(ruby.Annotation) : ruby.Annotation;
                    var baseM = measurer.Measure(baseText, style, s.WritingMode);
                    var rubyStyle = style.WithSize(style.FontSizePx * 0.5f);
                    var rubyM = measurer.Measure(rubyText, rubyStyle, s.WritingMode);
                    float w, h, adv, cross;
                    if (s.WritingMode == WritingMode.Horizontal)
                    {
                        w = Math.Max(baseM.Width, rubyM.Width);
                        h = baseM.Height + rubyM.Height * 0.85f;
                        adv = w;
                        cross = h;
                    }
                    else
                    {
                        // 字送り跟本字；注音再長也只伸進行間，不把本字拉去置中。
                        adv = Math.Max(baseM.Advance, style.FontSizePx * 0.85f);
                        float baseW = Math.Max(style.FontSizePx, Math.Min(baseM.Width, style.FontSizePx * 1.15f));
                        w = baseW;
                        h = adv;
                        cross = baseW;
                        list.Add(new Atom(w, h, adv, cross, ruby.BaseText.Length, style.FontSizePx,
                            new DrawRubyOp(baseText, rubyText, 0, 0, baseW, h, style, s.WritingMode)));
                        break;
                    }
                    list.Add(new Atom(w, h, adv, cross, ruby.BaseText.Length, style.FontSizePx, new DrawRubyOp(baseText, rubyText, 0, 0, w, h, style, s.WritingMode)));
                    break;
                }
                case TextRun run:
                    SplitRun(run, s, measurer, list);
                    break;
            }
        }
        return list;
    }

    static void SplitRun(TextRun run, LayoutSettings s, ITextMeasurer measurer, List<Atom> list)
    {
        var style = Scale(run.Style, s);
        var text = run.Text;
        int i = 0;
        while (i < text.Length)
        {
            int n = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
            // 連續 ASCII（含空白、引號）當一段英文；短且無空白才縱中橫，避免 "His" / birthday 被拆開。
            bool tcy = false;
            if (n == 1 && IsLatinPhraseChar(text[i]))
            {
                int j = i + 1;
                while (j < text.Length && IsLatinPhraseChar(text[j])) j++;
                while (j > i + 1 && text[j - 1] == ' ') j--;
                n = j - i;
                int letters = 0;
                bool spaced = false;
                for (int k = i; k < j; k++)
                {
                    if (text[k] == ' ') spaced = true;
                    else if (char.IsLetterOrDigit(text[k])) letters++;
                }
                tcy = s.WritingMode == WritingMode.Vertical && letters is >= 1 and <= 4 && !spaced;
            }
            var slice = text.Substring(i, n);
            if (s.WritingMode == WritingMode.Vertical && !tcy)
                slice = VerticalPunct.Apply(slice);
            var measureMode = tcy ? WritingMode.Horizontal : s.WritingMode;
            var m = measurer.Measure(slice, style, measureMode);
            float w = m.Width, h = m.Height, adv, cross;
            if (s.WritingMode == WritingMode.Horizontal)
            {
                adv = m.Advance;
                cross = h;
            }
            else if (tcy)
            {
                float em = style.FontSizePx;
                w = Math.Min(em, Math.Max(m.Width, em * 0.5f));
                h = em;
                adv = em;
                cross = em;
            }
            else
            {
                adv = Math.Max(m.Advance, style.FontSizePx * 0.85f);
                cross = Math.Max(w, style.FontSizePx);
            }
            list.Add(new Atom(w, h, adv, cross, slice.Length, style.FontSizePx, new DrawTextOp(slice, 0, 0, w, h, style, s.WritingMode, tcy)));
            i += n;
        }
    }

    static bool IsLatinPhraseChar(char c) =>
        c < 128 && !char.IsControl(c);

    static TextStyle Scale(TextStyle style, LayoutSettings s) => new()
    {
        FontSizePx = style.FontSizePx * s.FontScale,
        FontFamily = string.IsNullOrWhiteSpace(s.FontFamily) ? style.FontFamily : s.FontFamily,
        Bold = style.Bold,
        Italic = style.Italic,
        Emphasis = style.Emphasis,
        ColorArgb = s.InkArgb
    };

    static PageModel NewPage(int index, int spine, int offset, PageSpreadKind spread) => new()
    {
        Index = index,
        SpineIndex = spine,
        CharOffset = offset,
        Spread = spread
    };

    static string AtomText(Atom a) => a.Op switch
    {
        DrawTextOp t => t.Text,
        DrawRubyOp r => r.BaseText,
        _ => ""
    };

    static bool StartsKinsoku(string t) => t.Length > 0 && CannotStart.Contains(t[0]);
    static bool EndsKinsoku(string t) => t.Length > 0 && CannotEnd.Contains(t[^1]);

    sealed record Atom(float Width, float Height, float Advance, float Cross, int Chars, float Em, DrawOp? Op, bool Break = false)
    {
        public static Atom LineBreak() => new(0, 0, 0, 0, 0, 0, null, true);

        public DrawOp ToOp(float x, float y) => Op switch
        {
            DrawTextOp t => t with { X = x, Y = y },
            DrawRubyOp r => r with { X = x, Y = y },
            DrawImageOp i => i with { X = x, Y = y },
            _ => new DrawTextOp("", x, y, 0, 0, new TextStyle(), WritingMode.Horizontal, false)
        };
    }
}

