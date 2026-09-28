namespace EpubReader.Core;

public readonly record struct ImageSize(int Width, int Height);

public static class ImageSizeReader
{
    public static ImageSize? TryRead(byte[] data)
    {
        if (data.Length < 24) return null;
        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return Png(data);
        if (data[0] == 0xFF && data[1] == 0xD8)
            return Jpeg(data);
        if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46)
            return Gif(data);
        if (data[0] == 0x42 && data[1] == 0x4D)
            return Bmp(data);
        if (data.Length >= 16 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46
            && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
            return Webp(data);
        return null;
    }

    public static bool IsImageExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".jfif" => true,
        _ => false
    };

    static ImageSize? Png(byte[] d)
    {
        if (d.Length < 24) return null;
        int w = ReadBe32(d, 16);
        int h = ReadBe32(d, 20);
        return w > 0 && h > 0 ? new ImageSize(w, h) : null;
    }

    static ImageSize? Gif(byte[] d)
    {
        if (d.Length < 10) return null;
        int w = d[6] | (d[7] << 8);
        int h = d[8] | (d[9] << 8);
        return w > 0 && h > 0 ? new ImageSize(w, h) : null;
    }

    static ImageSize? Bmp(byte[] d)
    {
        if (d.Length < 26) return null;
        int w = BitConverter.ToInt32(d, 18);
        int h = Math.Abs(BitConverter.ToInt32(d, 22));
        return w > 0 && h > 0 ? new ImageSize(w, h) : null;
    }

    static ImageSize? Jpeg(byte[] d)
    {
        int i = 2;
        while (i + 9 < d.Length)
        {
            if (d[i] != 0xFF) return null;
            while (i < d.Length && d[i] == 0xFF) i++;
            if (i >= d.Length) return null;
            byte marker = d[i++];
            if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7))
                continue;
            if (i + 1 >= d.Length) return null;
            int len = (d[i] << 8) | d[i + 1];
            if (len < 2 || i + len > d.Length) return null;
            // SOF0-SOF3, SOF5-SOF7, SOF9-SOF11, SOF13-SOF15
            if ((marker >= 0xC0 && marker <= 0xC3) || (marker >= 0xC5 && marker <= 0xC7)
                || (marker >= 0xC9 && marker <= 0xCB) || (marker >= 0xCD && marker <= 0xCF))
            {
                if (i + 6 >= d.Length) return null;
                int h = (d[i + 3] << 8) | d[i + 4];
                int w = (d[i + 5] << 8) | d[i + 6];
                return w > 0 && h > 0 ? new ImageSize(w, h) : null;
            }
            i += len;
        }
        return null;
    }

    static ImageSize? Webp(byte[] d)
    {
        if (d.Length < 30) return null;
        // VP8X
        if (d[12] == 0x56 && d[13] == 0x50 && d[14] == 0x38 && d[15] == 0x58 && d.Length >= 30)
        {
            int w = 1 + (d[24] | (d[25] << 8) | (d[26] << 16));
            int h = 1 + (d[27] | (d[28] << 8) | (d[29] << 16));
            return new ImageSize(w, h);
        }
        // VP8L
        if (d[12] == 0x56 && d[13] == 0x50 && d[14] == 0x38 && d[15] == 0x4C && d.Length >= 25)
        {
            int b0 = d[21], b1 = d[22], b2 = d[23], b3 = d[24];
            int w = 1 + (b0 | ((b1 & 0x3F) << 8));
            int h = 1 + (((b1 & 0xC0) >> 6) | (b2 << 2) | ((b3 & 0x0F) << 10));
            return new ImageSize(w, h);
        }
        // VP8
        if (d[12] == 0x56 && d[13] == 0x50 && d[14] == 0x38 && d[15] == 0x20 && d.Length >= 30)
        {
            int w = d[26] | (d[27] << 8);
            int h = d[28] | (d[29] << 8);
            return new ImageSize(w & 0x3FFF, h & 0x3FFF);
        }
        return null;
    }

    static int ReadBe32(byte[] d, int o) => (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];
}
