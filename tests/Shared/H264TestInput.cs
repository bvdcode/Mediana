using System.Buffers.Binary;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class H264TestInput
    {
        public static async Task<(H264Configuration Configuration, byte[] Nal)> ReadIdrAsync(string path, int packetIndex)
        {
            await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            H264Configuration configuration = H264Configuration.Parse(reader.Streams[0].Configuration.Span, new H264DecoderOptions());
            for (int index = 0; ; index++)
            {
                using MediaPacket? packet = await reader.ReadPacketAsync();
                if (packet is null)
                {
                    throw new InvalidDataException("The reference video has no packet at the requested index.");
                }
                if (index < packetIndex)
                {
                    continue;
                }
                ReadOnlySpan<byte> remaining = packet.Data.Span;
                while (!remaining.IsEmpty)
                {
                    int length = configuration.LengthSize switch
                    {
                        1 => remaining[0],
                        2 => BinaryPrimitives.ReadUInt16BigEndian(remaining),
                        4 => checked((int)BinaryPrimitives.ReadUInt32BigEndian(remaining)),
                        _ => throw new InvalidDataException("The reference video has an invalid NAL length size.")
                    };
                    ReadOnlySpan<byte> nal = remaining.Slice(configuration.LengthSize, length);
                    if (H264Nal.Type(nal) == 5)
                    {
                        return (configuration, nal.ToArray());
                    }
                    remaining = remaining[(configuration.LengthSize + length)..];
                }
                throw new InvalidDataException("The reference packet has no IDR slice.");
            }
        }

        public static void VerifyPicture(H264Picture picture, ReadOnlySpan<byte> expected)
        {
            int offset = 0;
            for (int plane = 0; plane < 3; plane++)
            {
                int subsampling = plane == 0 ? 1 : 2;
                int width = picture.Sequence.Width / subsampling;
                int height = picture.Sequence.Height / subsampling;
                int cropX = picture.Sequence.CropLeft / subsampling;
                int cropY = picture.Sequence.CropTop / subsampling;
                for (int row = 0; row < height; row++)
                {
                    ReadOnlySpan<byte> decoded = picture.Plane(plane).Slice((cropY + row) * picture.Stride(plane) + cropX, width);
                    if (width > expected.Length - offset || !decoded.SequenceEqual(expected.Slice(offset, width)))
                    {
                        throw new InvalidDataException($"H.264 reference mismatch in plane {plane}, row {row}.");
                    }
                    offset += width;
                }
            }
            if (expected.Length != offset)
            {
                throw new InvalidDataException("The H.264 reference picture has an unexpected length.");
            }
        }
    }
}
