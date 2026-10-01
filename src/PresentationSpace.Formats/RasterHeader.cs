using System.Buffers.Binary;

namespace PresentationSpace.Formats;

/// <summary>Bounded raster-header metadata, not a full decoder or a claim that the compressed payload is valid.</summary>
public readonly record struct RasterHeader(int Width, int Height, string MimeType, string Extension)
{
    public static RasterHeader Read(ReadOnlySpan<byte> data)
    {
        if (!TryRead(data, out var header)) throw new InvalidDataException("Unsupported or malformed PNG, JPEG, GIF or WebP header.");
        return header;
    }
    public static bool TryRead(ReadOnlySpan<byte> data, out RasterHeader header)
    {
        header = default;
        if (data.Length >= 24 && data[..8].SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) && data.Slice(12,4).SequenceEqual("IHDR"u8))
        {
            uint w = BinaryPrimitives.ReadUInt32BigEndian(data[16..]), h = BinaryPrimitives.ReadUInt32BigEndian(data[20..]);
            if (BinaryPrimitives.ReadUInt32BigEndian(data[8..]) != 13 || w > int.MaxValue || h > int.MaxValue) return false;
            header = new((int)w, (int)h, "image/png", "png");
        }
        else if (data.Length >= 10 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8)))
            header = new(BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..]), "image/gif", "gif");
        else if (data.Length >= 4 && data[0] == 255 && data[1] == 216)
        {
            int p = 2;
            while (p < data.Length)
            {
                if (data[p++] != 255) return false;
                while (p < data.Length && data[p] == 255) p++;
                if (p >= data.Length) return false;
                byte marker = data[p++];
                if (marker is 0xDA or 0xD9) return false; // Never scan entropy-coded image data.
                if (marker == 1 || marker is >= 0xD0 and <= 0xD7) continue;
                if (data.Length - p < 2) return false;
                int size = BinaryPrimitives.ReadUInt16BigEndian(data[p..]);
                if (size < 2 || size > data.Length - p) return false;
                if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
                {
                    if (size < 8) return false;
                    header = new(BinaryPrimitives.ReadUInt16BigEndian(data[(p+5)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(p+3)..]), "image/jpeg", "jpg");
                    break;
                }
                p += size;
            }
        }
        else if (data.Length >= 20 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8,4).SequenceEqual("WEBP"u8))
        {
            long end = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) + 8L;
            if (end > data.Length || end < 20) return false;
            int p = 12;
            while (p + 8L <= end)
            {
                var tag = data.Slice(p,4); uint size = BinaryPrimitives.ReadUInt32LittleEndian(data[(p+4)..]);
                long next = p + 8L + size + (size & 1);
                if (next > end) return false;
                var payload = data.Slice(p+8, (int)size);
                if (tag.SequenceEqual("VP8X"u8) && size >= 10)
                    header = new(U24(payload[4..]) + 1, U24(payload[7..]) + 1, "image/webp", "webp");
                else if (tag.SequenceEqual("VP8 "u8) && size >= 10 && payload.Slice(3,3).SequenceEqual(new byte[] {0x9D,0x01,0x2A}))
                    header = new(BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(payload[8..]) & 0x3FFF, "image/webp", "webp");
                else if (tag.SequenceEqual("VP8L"u8) && size >= 5 && payload[0] == 0x2F)
                {
                    uint bits = BinaryPrimitives.ReadUInt32LittleEndian(payload[1..]);
                    header = new((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1, "image/webp", "webp");
                }
                if (header.Width != 0) break;
                p = (int)next;
            }
        }
        return header.Width > 0 && header.Height > 0;
    }
    private static int U24(ReadOnlySpan<byte> data) => data[0] | data[1] << 8 | data[2] << 16;
}
