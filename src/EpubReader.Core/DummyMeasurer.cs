namespace EpubReader.Core;

public sealed class DummyMeasurer : ITextMeasurer
{
    public MeasuredSize Measure(string text, TextStyle style, WritingMode mode)
    {
        float em = Math.Max(1, style.FontSizePx);
        float w = 0;
        foreach (var c in text)
            w += c < 256 ? em * 0.5f : em;
        if (w <= 0) w = em * 0.5f;
        return mode == WritingMode.Horizontal
            ? new MeasuredSize(w, em * 1.15f, w)
            : new MeasuredSize(em, w, w);
    }

    public void Dispose() { }
}
