namespace EpubReader.Core;

/// <summary>
/// 中日共用閱讀字型候選。實際是否安裝由 DirectWrite 端再解析。
/// </summary>
public static class CjkFonts
{
    public static readonly string[] All =
    [
        "Yu Mincho", "游明朝",
        "Yu Gothic", "游ゴシック", "Yu Gothic UI",
        "Meiryo", "メイリオ", "Meiryo UI",
        "MS Mincho", "ＭＳ 明朝",
        "MS Gothic", "ＭＳ ゴシック",
        "Microsoft JhengHei", "微軟正黑體", "Microsoft JhengHei UI",
        "Microsoft YaHei", "微软雅黑", "Microsoft YaHei UI",
        "PMingLiU", "新細明體",
        "MingLiU", "細明體",
        "MingLiU-ExtB", "細明體-ExtB",
        "Noto Serif CJK JP", "Noto Serif CJK TC",
        "Noto Sans CJK JP", "Noto Sans CJK TC",
        "Source Han Serif", "Source Han Sans",
        "BIZ UDMincho", "BIZ UDPMincho",
        "SimSun", "宋体",
        "Malgun Gothic",
        "Segoe UI"
    ];

    public static readonly string[] Japanese =
    [
        "Yu Mincho", "游明朝",
        "Yu Gothic", "游ゴシック", "Yu Gothic UI",
        "Meiryo", "メイリオ",
        "MS Mincho", "ＭＳ 明朝",
        "MS Gothic", "ＭＳ ゴシック",
        "Noto Serif CJK JP", "Noto Sans CJK JP",
        "BIZ UDMincho"
    ];

    public static readonly string[] TraditionalChinese =
    [
        "Microsoft JhengHei", "微軟正黑體", "Microsoft JhengHei UI",
        "PMingLiU", "新細明體",
        "MingLiU", "細明體",
        "MingLiU-ExtB", "細明體-ExtB",
        "Noto Serif CJK TC", "Noto Sans CJK TC"
    ];

    public static readonly string[] SimplifiedChinese =
    [
        "Microsoft YaHei", "微软雅黑", "Microsoft YaHei UI",
        "SimSun", "宋体",
        "Noto Sans CJK SC", "Source Han Sans"
    ];

    public static string DefaultFamily(string? language)
    {
        var l = (language ?? "").ToLowerInvariant();
        if (l.StartsWith("zh-hant") || l.StartsWith("zh-tw") || l.StartsWith("zh-hk"))
            return "Microsoft JhengHei";
        if (l.StartsWith("zh"))
            return "Microsoft YaHei";
        return "Yu Mincho";
    }

    public static string Locale(string? language)
    {
        var l = (language ?? "").ToLowerInvariant();
        if (l.StartsWith("zh-hant") || l.StartsWith("zh-tw") || l.StartsWith("zh-hk"))
            return "zh-Hant";
        if (l.StartsWith("zh"))
            return "zh-Hans";
        if (l.StartsWith("ko"))
            return "ko-KR";
        return "ja-JP";
    }
}
