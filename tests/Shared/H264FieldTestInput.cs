// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal static class H264FieldTestInput
    {
        public static async Task VerifyIntraAsync(string root, string name, int packet)
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Path.Combine(root, name + ".mp4"), packet);
            byte[] expected = await ReadReferenceAsync(root, name, packet / 2, configuration.Sequences.Values.Single());
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
            if (header.Kind != H264SliceKind.I || header.Structure != H264PictureStructure.TopField)
            {
                throw new InvalidDataException("Expected an independently encoded intra top field.");
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264Picture picture = new(header.Sequence, allocator, header.Kind, header.Structure))
            {
                if (header.Picture.Cabac)
                {
                    H264CabacPictureDecoder.DecodeSlice(ref bits, header, new([], []), new(0, 0, header.Structure),
                        picture, header.Layout.MacroblockCount, allocator);
                }
                else
                {
                    H264PictureDecoder.DecodeSlice(ref bits, header, [], picture, header.Layout.MacroblockCount);
                }
                H264Deblocking.Apply(picture, header, default);
                VerifyPicture(picture, expected);
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Field reconstruction retained its buffers.");
            }
        }

        public static Task VerifySequenceAsync(string root, string name, IReadOnlyList<int>? referenceFrames = null) =>
            VerifySequenceAsync(root, name, referenceFrames, false);

        public static Task VerifyMixedSequenceAsync(string root, string name) => VerifySequenceAsync(root, name, null, true);

        private static async Task VerifySequenceAsync(string root, string name, IReadOnlyList<int>? referenceFrames, bool mixed)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264ReferenceBuffer references = new())
            using (H264FrameQueue outputs = new(allocator))
            {
                int published = 0;
                H264PictureOrderState order = new();
                int packetCount = mixed ? 33 : 24;
                for (int packet = 0; packet < packetCount; packet++)
                {
                    (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Path.Combine(root, name + ".mp4"), packet);
                    int displayIndex = mixed ? packet switch
                    {
                        < 8 => packet,
                        < 26 => 8 + (packet - 8) / 2,
                        _ => packet - 9
                    } : referenceFrames is null ? packet / 2 : referenceFrames[packet / 2];
                    byte[] expected = await ReadReferenceAsync(root, name, displayIndex, configuration.Sequences.Values.Single());
                    order = DecodeAndStore(configuration, nal, expected, order, references, allocator, packet, outputs, displayIndex);
                    if (packet == packetCount - 1) { outputs.Drain(); }
                    while (outputs.Receive() is VideoFrame frame)
                    {
                        using (frame)
                        {
                            byte[] display = await ReadReferenceAsync(root, name, published, configuration.Sequences.Values.Single());
                            VideoScanMode scan = mixed && published is < 8 or >= 17 ? VideoScanMode.Progressive : VideoScanMode.TopFieldFirst;
                            VerifyOutput(frame, display, published, scan);
                            published++;
                        }
                    }
                }
                if (published != (mixed ? 24 : 12)) { throw new InvalidDataException("Incorrect number of assembled field pairs."); }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Field reference decoding retained its buffers.");
            }
        }

        private static H264PictureOrderState DecodeAndStore(H264Configuration configuration, ReadOnlySpan<byte> nal,
            ReadOnlySpan<byte> expected, H264PictureOrderState order, H264ReferenceBuffer references, MediaBufferAllocator allocator, int packet,
            H264FrameQueue outputs, int displayIndex)
        {
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
            H264PictureOrderState nextOrder = order.Advance(header);
            H264BidirectionalReferences lists = header.Kind switch
            {
                H264SliceKind.I or H264SliceKind.P => new(references.GetList0(header), []),
                H264SliceKind.B => references.GetBidirectionalLists(header, nextOrder.Current),
                _ => throw new InvalidDataException("Unexpected field slice type.")
            };
            H264Picture picture = new(header.Sequence, allocator, header.Kind, header.Structure);
            bool stored = false;
            try
            {
                picture.CaptureReferences(0, lists.L0, lists.L1);
                if (header.Picture.Cabac)
                {
                    H264CabacPictureDecoder.DecodeSlice(ref bits, header, lists, nextOrder.Current,
                        picture, header.Layout.MacroblockCount, allocator);
                }
                else if (header.Kind == H264SliceKind.B)
                {
                    H264BidirectionalPictureDecoder.DecodeSlice(ref bits, header, lists, nextOrder.Current,
                        picture, header.Layout.MacroblockCount);
                }
                else
                {
                    H264PictureDecoder.DecodeSlice(ref bits, header, lists.L0, picture, header.Layout.MacroblockCount);
                }
                H264Deblocking.Apply(picture, [new H264SliceRegion(header, header.Layout.MacroblockCount)], default);
                VerifyPicture(picture, expected, packet);
                int parity = header.Structure == H264PictureStructure.BottomField ? 1 : 0;
                int duration = header.Structure == H264PictureStructure.Frame ? 2 : 1;
                VideoFrame frame = H264FramePublisher.Publish(picture, new(displayIndex * 2 + parity, new(1, 24)),
                    new(duration, new(1, 24)), default);
                bool queued = false;
                try
                {
                    references.Store(picture, header, nextOrder.Current);
                    stored = true;
                    outputs.Add(frame, nextOrder.Current, header);
                    queued = true;
                }
                finally
                {
                    if (!queued) { frame.Dispose(); }
                }
                return nextOrder;
            }
            finally
            {
                if (!stored)
                {
                    picture.Dispose();
                }
            }
        }

        private static void VerifyOutput(VideoFrame frame, ReadOnlySpan<byte> expected, int index, VideoScanMode scan)
        {
            if (frame.Format.ScanMode != scan || frame.Timestamp != new MediaTimestamp(index, new(1, 12)) ||
                frame.Duration is not MediaDuration duration || new MediaTimestamp(duration.Value, duration.TimeBase) != new MediaTimestamp(1, new(1, 12)))
            {
                throw new InvalidDataException("Assembled field presentation order or timing differs from the source.");
            }
            int offset = 0;
            for (int plane = 0; plane < frame.Planes.Count; plane++)
            {
                for (int row = 0; row < frame.Planes[plane].Height; row++)
                {
                    ReadOnlySpan<byte> actual = frame.GetRow(plane, row);
                    if (!actual.SequenceEqual(expected.Slice(offset, actual.Length)))
                    {
                        throw new InvalidDataException($"Assembled field mismatch in frame {index}, plane {plane}, row {row}.");
                    }
                    offset += actual.Length;
                }
            }
            if (offset != expected.Length) { throw new InvalidDataException("Unexpected assembled frame dimensions."); }
        }

        private static void VerifyPicture(H264Picture picture, ReadOnlySpan<byte> expected, int packet = 0)
        {
            int parity = picture.Structure switch
            {
                H264PictureStructure.Frame or H264PictureStructure.TopField => 0,
                H264PictureStructure.BottomField => 1,
                _ => throw new InvalidDataException("Expected a field picture.")
            };
            int rowStep = picture.Layout.IsField ? 2 : 1;
            int referenceOffset = 0;
            for (int plane = 0; plane < 3; plane++)
            {
                int width = picture.Stride(plane) * picture.SampleBytes;
                int rows = picture.Height / picture.VerticalScale(plane);
                for (int row = 0; row < rows; row++)
                {
                    ReadOnlySpan<byte> actual = picture.ReadPlane(plane).Slice(row * width, width);
                    if (!actual.SequenceEqual(expected.Slice(referenceOffset + (row * rowStep + parity) * width, width)))
                    {
                        throw new InvalidDataException($"Field mismatch in packet {packet}, plane {plane}, row {row}.");
                    }
                }
                referenceOffset += rows * rowStep * width;
            }
            if (referenceOffset != expected.Length)
            {
                throw new InvalidDataException("Unexpected field reference image dimensions.");
            }
        }

        private static async Task<byte[]> ReadReferenceAsync(string root, string name, int frame, H264SequenceParameterSet sequence)
        {
            await using FileStream input = File.OpenRead(Path.Combine(root, name + ".yuv.gz"));
            await using GZipStream archive = new(input, CompressionMode.Decompress);
            int lumaBytes = sequence.CodedWidth * sequence.CodedHeight * (sequence.LumaBitDepth <= 8 ? 1 : 2);
            int chromaBytes = sequence.ChromaFormat switch
            {
                1 => lumaBytes / 2,
                2 => lumaBytes,
                _ => throw new InvalidDataException("Unsupported reference image chroma geometry.")
            };
            byte[] expected = new byte[lumaBytes + chromaBytes];
            for (int index = 0; index <= frame; index++)
            {
                await archive.ReadExactlyAsync(expected);
            }
            return expected;
        }
    }
}
