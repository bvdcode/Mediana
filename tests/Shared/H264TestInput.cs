using System.Buffers.Binary;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class H264TestInput
    {
        public static void VerifyFrame(VideoFrame frame, ReadOnlySpan<byte> expected)
        {
            int offset = 0;
            for (int plane = 0; plane < frame.Planes.Count; plane++)
            {
                for (int row = 0; row < frame.Planes[plane].Height; row++)
                {
                    ReadOnlySpan<byte> actual = frame.GetRow(plane, row);
                    if (actual.Length > expected.Length - offset || !actual.SequenceEqual(expected.Slice(offset, actual.Length)))
                    {
                        throw new InvalidDataException($"H.264 output mismatch in plane {plane}, row {row}.");
                    }
                    offset += actual.Length;
                }
            }
            if (offset != expected.Length)
            {
                throw new InvalidDataException("The H.264 output frame has an unexpected length.");
            }
        }

        public static void VerifyAndStorePicture(ReadOnlySpan<byte> nal, H264Configuration configuration,
            H264ReferenceBuffer references, MediaBufferAllocator allocator, ReadOnlySpan<byte> expected)
        {
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
            H264Picture picture = H264PictureDecoder.Decode(ref bits, header, references.GetList0(header), allocator);
            bool stored = false;
            try
            {
                VerifyPicture(picture, expected, header.FrameNumber);
                references.Store(picture, header);
                stored = true;
            }
            finally
            {
                if (!stored)
                {
                    picture.Dispose();
                }
            }
        }

        public static async Task<(H264Configuration Configuration, byte[] Nal)> ReadIdrAsync(string path, int packetIndex)
        {
            (H264Configuration configuration, byte[] nal) = await ReadSliceAsync(path, packetIndex);
            if (H264Nal.Type(nal) != 5)
            {
                throw new InvalidDataException("The reference packet has no IDR slice.");
            }
            return (configuration, nal);
        }

        public static async Task<(H264Configuration Configuration, byte[] Nal)> ReadSliceAsync(string path, int packetIndex)
        {
            await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            H264Configuration configuration = H264Configuration.Parse(reader.Streams[0].Configuration.Span, new H264DecoderOptions());
            int index = 0;
            while (true)
            {
                using MediaPacket? packet = await reader.ReadPacketAsync();
                if (packet is null)
                {
                    throw new InvalidDataException("The reference video has no packet at the requested index.");
                }
                if (packet.StreamIndex != reader.Streams[0].StreamIndex)
                {
                    continue;
                }
                if (index++ < packetIndex)
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
                    if (H264Nal.Type(nal) is 1 or 5)
                    {
                        return (configuration, nal.ToArray());
                    }
                    remaining = remaining[(configuration.LengthSize + length)..];
                }
                throw new InvalidDataException("The reference packet has no slice.");
            }
        }

        public static void VerifyPicture(H264Picture picture, ReadOnlySpan<byte> expected, int? frameNumber = null)
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
                        throw new InvalidDataException($"H.264 reference mismatch in frame {frameNumber}, plane {plane}, row {row}.");
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
