namespace EpubReader.Core;

/// <summary>
/// 把橫排標點換成 Unicode 直式呈現形，避免字型沒有 vert 時長音、省略號、括號躺著。
/// </summary>
public static class VerticalPunct
{
    public static string Apply(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        Span<char> buf = text.Length <= 8 ? stackalloc char[text.Length] : new char[text.Length];
        bool changed = false;
        for (int i = 0; i < text.Length; i++)
        {
            char mapped = Map(text[i]);
            buf[i] = mapped;
            if (mapped != text[i]) changed = true;
        }
        return changed ? new string(buf) : text;
    }

    public static char Map(char c) => c switch
    {
        '…' => '︙', // ︙
        '‥' => '︰', // ︰
        '—' or '―' or '–' or 'ー' or '‐' or '−' => '︱', // ︱
        '〜' or '～' => '︲',
        '（' => '︵', // ︵
        '）' => '︶', // ︶
        '「' => '﹁', // ﹁
        '」' => '﹂', // ﹂
        '『' => '﹃', // ﹃
        '』' => '﹄', // ﹄
        '【' => '︻',
        '】' => '︼',
        '［' or '[' => '﹇',
        '］' or ']' => '﹈',
        '｛' => '︷',
        '｝' => '︸',
        '〈' => '︿',
        '〉' => '﹀',
        '《' => '︽',
        '》' => '︾',
        '〔' => '︹',
        '〕' => '︺',
        '：' => '︓',
        '；' => '︔',
        '！' => '︕',
        '？' => '︖',
        '、' => '︑',
        '。' => '︒',
        _ => c
    };
}
