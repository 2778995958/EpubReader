using System.Runtime.InteropServices;
using EpubReader.Core;
using Vortice.DirectWrite;

namespace EpubReader.Rendering;

sealed class CjkFontFallback : IDisposable
{
    readonly IDWriteFactory2 _factory;
    readonly IDWriteFontCollection _fonts;
    readonly HashSet<string> _installed = new(StringComparer.OrdinalIgnoreCase);
    IDWriteFontFallback? _fallback;

    public string Family { get; }
    public string Locale { get; }

    public CjkFontFallback(IDWriteFactory2 factory, string? preferred, string? language)
    {
        _factory = factory;
        _fonts = factory.GetSystemFontCollection(false);
        CacheInstalled();
        Locale = CjkFonts.Locale(language);
        Family = Resolve(preferred, language);
        _fallback = BuildFallback();
    }

    public void Apply(IDWriteTextFormat format)
    {
        if (_fallback != null && format is IDWriteTextFormat1 f1)
        {
            try { f1.FontFallback = _fallback; }
            catch { /* 舊系統沒有 fallback 也不擋繪製 */ }
        }
    }

    public string ResolveFamily(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested) && IsInstalled(requested))
            return requested;
        return Family;
    }

    void CacheInstalled()
    {
        foreach (var name in CjkFonts.All)
        {
            if (IsInstalled(name))
                _installed.Add(name);
        }
    }

    bool IsInstalled(string name)
    {
        if (_installed.Contains(name)) return true;
        _fonts.FindFamilyName(name, out uint index);
        return index != uint.MaxValue;
    }

    string Resolve(string? preferred, string? language)
    {
        foreach (var name in Candidates(preferred, language))
        {
            if (IsInstalled(name))
                return name;
        }
        return "Segoe UI";
    }

    static IEnumerable<string> Candidates(string? preferred, string? language)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
            yield return preferred;
        var l = (language ?? "").ToLowerInvariant();
        IEnumerable<string> first =
            l.StartsWith("zh-hant") || l.StartsWith("zh-tw") || l.StartsWith("zh-hk")
                ? CjkFonts.TraditionalChinese.Concat(CjkFonts.Japanese).Concat(CjkFonts.SimplifiedChinese)
                : l.StartsWith("zh")
                    ? CjkFonts.SimplifiedChinese.Concat(CjkFonts.TraditionalChinese).Concat(CjkFonts.Japanese)
                    : CjkFonts.Japanese.Concat(CjkFonts.TraditionalChinese).Concat(CjkFonts.SimplifiedChinese);
        foreach (var n in first.Concat(CjkFonts.All))
            yield return n;
    }

    IDWriteFontFallback? BuildFallback()
    {
        try
        {
            using var builder = _factory.CreateFontFallbackBuilder();
            var jp = Installed(CjkFonts.Japanese);
            var tc = Installed(CjkFonts.TraditionalChinese);
            var sc = Installed(CjkFonts.SimplifiedChinese);
            var han = Distinct(tc.Concat(sc).Concat(jp).Concat(Installed(["MingLiU-ExtB", "細明體-ExtB", "SimSun"])));
            var hangul = Installed(["Malgun Gothic", "Gulim", "Batang"]);

            if (jp.Length > 0)
            {
                Map(builder, jp,
                    R(0x3040, 0x309F),
                    R(0x30A0, 0x30FF),
                    R(0x31F0, 0x31FF),
                    R(0xFF66, 0xFF9D),
                    R(0x1B000, 0x1B16F)
                );
            }
            if (han.Length > 0)
            {
                Map(builder, han,
                    R(0x2E80, 0x2EFF),
                    R(0x2F00, 0x2FDF),
                    R(0x3000, 0x303F),
                    R(0x31C0, 0x31EF),
                    R(0x3400, 0x4DBF),
                    R(0x4E00, 0x9FFF),
                    R(0xF900, 0xFAFF),
                    R(0xFE10, 0xFE1F),
                    R(0xFE30, 0xFE4F),
                    R(0xFF01, 0xFF60),
                    R(0x20000, 0x2A6DF),
                    R(0x2A700, 0x2B73F),
                    R(0x2B740, 0x2B81F),
                    R(0x2B820, 0x2CEAF),
                    R(0x2CEB0, 0x2EBEF)
                );
            }
            var bopo = Distinct(tc.Concat(sc));
            if (bopo.Length > 0)
                Map(builder, bopo, R(0x3100, 0x312F), R(0x31A0, 0x31BF));
            if (hangul.Length > 0)
                Map(builder, hangul, R(0x1100, 0x11FF), R(0x3130, 0x318F), R(0xAC00, 0xD7AF));

            var system = _factory.SystemFontFallback;
            builder.AddMappings(system);
            return builder.CreateFontFallback();
        }
        catch
        {
            return null;
        }
    }

    string[] Installed(IEnumerable<string> names)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in names)
        {
            if (!seen.Add(n)) continue;
            if (IsInstalled(n))
                list.Add(n);
        }
        return [.. list];
    }

    static string[] Distinct(IEnumerable<string> names)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in names)
        {
            if (seen.Add(n))
                list.Add(n);
        }
        return [.. list];
    }

    static UnicodeRange R(int first, int last) => new() { First = (uint)first, Last = (uint)last };

    static void Map(IDWriteFontFallbackBuilder builder, string[] families, params UnicodeRange[] ranges)
    {
        if (families.Length == 0 || ranges.Length == 0) return;
        var namePtrs = new IntPtr[families.Length];
        var array = IntPtr.Zero;
        try
        {
            for (int i = 0; i < families.Length; i++)
                namePtrs[i] = Marshal.StringToHGlobalUni(families[i]);
            array = Marshal.AllocHGlobal(IntPtr.Size * families.Length);
            Marshal.Copy(namePtrs, 0, array, families.Length);
            builder.AddMapping(ranges, (uint)ranges.Length, array, (uint)families.Length, null, null, null, 1f);
        }
        finally
        {
            if (array != IntPtr.Zero)
                Marshal.FreeHGlobal(array);
            foreach (var p in namePtrs)
            {
                if (p != IntPtr.Zero)
                    Marshal.FreeHGlobal(p);
            }
        }
    }

    public void Dispose()
    {
        _fallback?.Dispose();
        _fallback = null;
        _fonts.Dispose();
        GC.SuppressFinalize(this);
    }
}
