using System.Buffers.Binary;
using System.IO.Compression;

namespace SpawnDev.AI.Server;

/// <summary>
/// Minimal dependency-free PNG encoder (RGBA8 → PNG, zlib/deflate via System.IO.Compression).
/// Serves the image endpoints/tools on desktop AND Blazor WASM without an imaging package - the
/// bytes are already RGBA from the pipeline; PNG is just framing + lossless compression.
/// </summary>
public static class PngEncoder
{
    /// <summary>Encode RGBA8 pixels (4 bytes/px, row-major) as a PNG.</summary>
    public static byte[] EncodeRgba(byte[] rgba, int width, int height)
    {
        if (rgba.Length < width * height * 4)
            throw new ArgumentException($"rgba length {rgba.Length} < {width}x{height}x4");

        // Raw scanlines with filter byte 0 (None) per row.
        var raw = new byte[height * (1 + width * 4)];
        for (int y = 0; y < height; y++)
        {
            int src = y * width * 4, dst = y * (1 + width * 4);
            raw[dst] = 0;
            Buffer.BlockCopy(rgba, src, raw, dst + 1, width * 4);
        }

        using var ms = new MemoryStream();
        // Explicit signature bytes: a "\x89..."u8 literal UTF-8-encodes 0x89 as C2 89 (caught by
        // the first decoded fox, 2026-07-04).
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        // IHDR
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8;    // bit depth
        ihdr[9] = 6;    // color type RGBA
        ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(ms, "IHDR", ihdr);

        // IDAT (zlib: 0x78 0x9C header + deflate + adler32)
        using (var idat = new MemoryStream())
        {
            idat.WriteByte(0x78); idat.WriteByte(0x9C);
            using (var deflate = new DeflateStream(idat, CompressionLevel.Fastest, leaveOpen: true))
                deflate.Write(raw);
            Span<byte> adler = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(adler, Adler32(raw));
            idat.Write(adler);
            WriteChunk(ms, "IDAT", idat.ToArray());
        }

        WriteChunk(ms, "IEND", ReadOnlySpan<byte>.Empty);
        return ms.ToArray();
    }

    private static void WriteChunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        Span<byte> typeBytes = stackalloc byte[4];
        for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Crc32(typeBytes, data);
        Span<byte> crcB = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcB, crc);
        s.Write(crcB);
    }

    private static uint Adler32(ReadOnlySpan<byte> data)
    {
        uint a = 1, b = 0;
        foreach (var t in data) { a = (a + t) % 65521; b = (b + a) % 65521; }
        return (b << 16) | a;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();
    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc32(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        uint c = 0xFFFFFFFFu;
        foreach (var x in a) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (var x in b) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
