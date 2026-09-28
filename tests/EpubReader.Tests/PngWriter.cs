using System.IO.Compression;
using System.Text;

namespace EpubReader.Tests;

static class PngWriter
{
    public static byte[] Make(int w, int h, byte r, byte g, byte b)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(ms, "IHDR", Buf =>
        {
            Be32(Buf, w);
            Be32(Buf, h);
            Buf.WriteByte(8);
            Buf.WriteByte(2);
            Buf.WriteByte(0);
            Buf.WriteByte(0);
            Buf.WriteByte(0);
        });
        WriteChunk(ms, "IDAT", Buf =>
        {
            using var raw = new MemoryStream();
            for (int y = 0; y < h; y++)
            {
                raw.WriteByte(0);
                for (int x = 0; x < w; x++)
                {
                    raw.WriteByte(r);
                    raw.WriteByte(g);
                    raw.WriteByte(b);
                }
            }
            using var z = new ZLibStream(Buf, CompressionLevel.Fastest, true);
            raw.Position = 0;
            raw.CopyTo(z);
        });
        WriteChunk(ms, "IEND", _ => { });
        return ms.ToArray();
    }

    static void WriteChunk(Stream s, string type, Action<Stream> body)
    {
        using var payload = new MemoryStream();
        payload.Write(Encoding.ASCII.GetBytes(type));
        body(payload);
        var bytes = payload.ToArray();
        Be32(s, bytes.Length - 4);
        s.Write(bytes);
        Be32(s, (int)Crc(bytes));
    }

    static void Be32(Stream s, int v)
    {
        s.WriteByte((byte)(v >> 24));
        s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    static uint Crc(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (var b in data)
        {
            c ^= b;
            for (int i = 0; i < 8; i++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFF;
    }
}

static class BmpWriter
{
    public static byte[] Make(int w, int h, byte r, byte g, byte b)
    {
        int row = ((w * 3 + 3) / 4) * 4;
        int pixels = row * h;
        int size = 54 + pixels;
        var d = new byte[size];
        d[0] = (byte)'B'; d[1] = (byte)'M';
        BitConverter.GetBytes(size).CopyTo(d, 2);
        BitConverter.GetBytes(54).CopyTo(d, 10);
        BitConverter.GetBytes(40).CopyTo(d, 14);
        BitConverter.GetBytes(w).CopyTo(d, 18);
        BitConverter.GetBytes(h).CopyTo(d, 22);
        BitConverter.GetBytes((short)1).CopyTo(d, 26);
        BitConverter.GetBytes((short)24).CopyTo(d, 28);
        for (int y = 0; y < h; y++)
        {
            int o = 54 + y * row;
            for (int x = 0; x < w; x++)
            {
                d[o++] = b; d[o++] = g; d[o++] = r;
            }
        }
        return d;
    }
}
