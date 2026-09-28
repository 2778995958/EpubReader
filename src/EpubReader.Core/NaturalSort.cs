namespace EpubReader.Core;

public static class NaturalSort
{
    public static int Compare(string? a, string? b)
    {
        a ??= "";
        b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int c = CompareDigits(a, ref i, b, ref j);
                if (c != 0) return c;
                continue;
            }
            int cc = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
            if (cc != 0) return cc;
            i++;
            j++;
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    static int CompareDigits(string a, ref int i, string b, ref int j)
    {
        while (i < a.Length && a[i] == '0') i++;
        while (j < b.Length && b[j] == '0') j++;
        int is_ = i, js = j;
        while (i < a.Length && char.IsDigit(a[i])) i++;
        while (j < b.Length && char.IsDigit(b[j])) j++;
        int lenA = i - is_, lenB = j - js;
        if (lenA != lenB) return lenA.CompareTo(lenB);
        int c = string.CompareOrdinal(a, is_, b, js, lenA);
        if (c != 0) return c;
        return 0;
    }
}
