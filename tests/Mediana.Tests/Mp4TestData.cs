using System.Buffers.Binary;
using System.Text;

namespace Mediana.Tests
{
    internal static class Mp4TestData
    {
        public static string PathFor(string name) => Path.Combine(AppContext.BaseDirectory, "Data", "Mp4", name);
        public static Task<byte[]> ReadAsync(string name = "avc-aac.mp4") => File.ReadAllBytesAsync(PathFor(name));

        public static int PayloadOffset(byte[] data, string boxType)
        {
            int offset = data.AsSpan().IndexOf(Encoding.Latin1.GetBytes(boxType));
            Assert.True(offset >= 4, $"The fixture has no '{boxType}' box.");
            return offset + 4;
        }

        public static void SetUInt32(byte[] data, string boxType, int fieldOffset, uint value) =>
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(PayloadOffset(data, boxType) + fieldOffset, 4), value);

        public static byte[] Use64BitChunkOffsets(byte[] data)
        {
            int payload = PayloadOffset(data, "stco");
            int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(payload + 4, 4));
            int oldSize = 16 + count * 4;
            byte[] replacement = new byte[16 + count * 8];
            BinaryPrimitives.WriteUInt32BigEndian(replacement, (uint)replacement.Length);
            "co64"u8.CopyTo(replacement.AsSpan(4));
            BinaryPrimitives.WriteUInt32BigEndian(replacement.AsSpan(12), (uint)count);
            for (int index = 0; index < count; index++)
            {
                uint offset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(payload + 8 + index * 4, 4));
                BinaryPrimitives.WriteUInt64BigEndian(replacement.AsSpan(16 + index * 8), offset);
            }
            int delta = replacement.Length - oldSize;
            foreach (string parent in new[] { "moov", "trak", "mdia", "minf", "stbl" })
            {
                int parentOffset = PayloadOffset(data, parent) - 8;
                uint size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(parentOffset, 4));
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(parentOffset, 4), (uint)(size + delta));
            }
            int start = payload - 8;
            return [.. data.AsSpan(0, start), .. replacement, .. data.AsSpan(start + oldSize)];
        }

        public static byte[] Use64BitTimeHeaders(byte[] data)
        {
            foreach (string type in new[] { "mvhd", "tkhd", "mdhd" })
            {
                int payload = PayloadOffset(data, type);
                int oldSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(payload - 8, 4));
                Assert.Equal(0, data[payload]);
                byte[] replacement = new byte[oldSize + 12];
                BinaryPrimitives.WriteUInt32BigEndian(replacement, (uint)replacement.Length);
                data.AsSpan(payload - 4, 4).CopyTo(replacement.AsSpan(4));
                Span<byte> fields = replacement.AsSpan(8);
                fields[0] = 1;
                data.AsSpan(payload + 1, 3).CopyTo(fields[1..]);
                BinaryPrimitives.WriteUInt64BigEndian(fields[4..], BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(payload + 4, 4)));
                BinaryPrimitives.WriteUInt64BigEndian(fields[12..], BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(payload + 8, 4)));
                int durationOffset = type == "tkhd" ? 20 : 16;
                data.AsSpan(payload + 12, durationOffset - 12).CopyTo(fields[20..]);
                BinaryPrimitives.WriteUInt64BigEndian(fields[(durationOffset + 8)..],
                    BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(payload + durationOffset, 4)));
                data.AsSpan(payload + durationOffset + 4, oldSize - 8 - durationOffset - 4)
                    .CopyTo(fields[(durationOffset + 16)..]);
                string[] parents = type switch
                {
                    "mvhd" => ["moov"],
                    "tkhd" => ["moov", "trak"],
                    "mdhd" => ["moov", "trak", "mdia"],
                    _ => throw new InvalidOperationException()
                };
                foreach (string parent in parents)
                {
                    int start = PayloadOffset(data, parent) - 8;
                    uint size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(start, 4));
                    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(start, 4), size + 12);
                }
                data = [.. data.AsSpan(0, payload - 8), .. replacement, .. data.AsSpan(payload - 8 + oldSize)];
            }
            return data;
        }
    }
}
