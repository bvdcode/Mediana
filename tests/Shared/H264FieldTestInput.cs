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
        public static async Task<int> VerifyMbaffIntraAsync(string root, string name, bool requireFields, bool require8x8 = false)
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Path.Combine(root, name + ".mp4"), 0);
            byte[] expected = await ReadReferenceAsync(root, name, 0, configuration.Sequences.Values.Single());
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
            if (header.Kind != H264SliceKind.I || !header.Layout.MacroblockAdaptiveFrameField || header.DeblockingEnabled)
            {
                throw new InvalidDataException("Expected an independently encoded MBAFF intra picture without deblocking.");
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            int fieldPairs = 0;
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
                VerifyPicture(picture, expected);
                if (require8x8 && picture.Transforms8x8.ContainsAnyExcept((byte)1))
                {
                    throw new InvalidDataException("Expected every MBAFF macroblock to use an 8x8 transform.");
                }
                for (int address = 0; address < header.Layout.MacroblockCount; address += 2)
                {
                    bool field = picture.Grid.IsField(address);
                    if (field) { fieldPairs++; }
                    if (picture.Grid.IsField(address + 1) != field || (requireFields && !field))
                    {
                        throw new InvalidDataException("The independently encoded MBAFF pair mode was not preserved.");
                    }
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("MBAFF intra reconstruction retained its buffers.");
            }
            return fieldPairs;
        }

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
            VerifySequenceAsync(root, name, referenceFrames, false, null);

        public static Task VerifyMixedSequenceAsync(string root, string name) => VerifySequenceAsync(root, name, null, true, null);

        public static async Task VerifyMixedBidirectionalSequenceAsync(string root, string name)
        {
            string[] lines = await File.ReadAllLinesAsync(Path.Combine(root, name + ".poc"));
            int[] order = lines.Select(value => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            await VerifySequenceAsync(root, name, null, true, order);
        }

        private static async Task VerifySequenceAsync(string root, string name, IReadOnlyList<int>? referenceFrames,
            bool mixed, IReadOnlyList<int>? mixedOrder)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            H264SequenceParameterSet? sequence = null;
            using (H264ReferenceBuffer references = new())
            using (H264FrameQueue outputs = new(allocator))
            {
                int published = 0;
                H264PictureOrderState order = new();
                int packetCount = mixedOrder?.Count ?? (mixed ? 33 : 24);
                Dictionary<int, VideoScanMode> scanModes = [];
                for (int packet = 0; packet < packetCount; packet++)
                {
                    (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Path.Combine(root, name + ".mp4"), packet);
                    sequence ??= configuration.Sequences.Values.Single();
                    int displayIndex = mixedOrder is not null ? mixedOrder[packet] / 2 : mixed ? packet switch
                    {
                        < 8 => packet,
                        < 26 => 8 + (packet - 8) / 2,
                        _ => packet - 9
                    } : referenceFrames is null ? packet / 2 : referenceFrames[packet / 2];
                    byte[] expected = await ReadReferenceAsync(root, name, displayIndex, configuration.Sequences.Values.Single());
                    order = DecodeAndStore(configuration, nal, expected, order, references, allocator, packet, outputs, displayIndex, out VideoScanMode scan);
                    scanModes[displayIndex] = scan;
                    if (mixedOrder is not null && order.Current.Value != mixedOrder[packet])
                    {
                        throw new InvalidDataException("Mixed picture order differs from the independent header trace.");
                    }
                    if (packet == packetCount - 1) { outputs.Drain(); }
                    while (outputs.Receive() is VideoFrame frame)
                    {
                        using (frame)
                        {
                            byte[] display = await ReadReferenceAsync(root, name, published, configuration.Sequences.Values.Single());
                            VerifyOutput(frame, display, published, scanModes[published]);
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
            await VerifyPublicAsync(root, name, mixed, sequence ?? throw new InvalidDataException("Missing field sequence."));
        }

        private static Task VerifyPublicAsync(string root, string name, bool mixed, H264SequenceParameterSet sequence)
        {
            int vertical = sequence.ChromaFormat switch
            {
                1 => 2,
                2 => 1,
                _ => throw new InvalidDataException("Unsupported reference chroma geometry.")
            };
            int count = mixed ? 24 : 12;
            VideoScanMode[] scans = Enumerable.Range(0, count).Select(index => (mixed, index) switch
            {
                (false, _) or (true, >= 8 and < 17) => VideoScanMode.TopFieldFirst,
                (true, _) => VideoScanMode.Progressive
            }).ToArray();
            return H264ProfileSequenceTestInput.VerifyAsync(Path.Combine(root, name),
                new PlanarVideoPixelFormat("reference-yuv", PlanarColorModel.Yuv, sequence.LumaBitDepth, 2, vertical),
                sequence.Width, sequence.Height, count, 12, scans);
        }

        private static H264PictureOrderState DecodeAndStore(H264Configuration configuration, ReadOnlySpan<byte> nal,
            ReadOnlySpan<byte> expected, H264PictureOrderState order, H264ReferenceBuffer references, MediaBufferAllocator allocator, int packet,
            H264FrameQueue outputs, int displayIndex, out VideoScanMode scan)
        {
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
            scan = header.Structure switch
            {
                H264PictureStructure.Frame => VideoScanMode.Progressive,
                H264PictureStructure.TopField or H264PictureStructure.BottomField => VideoScanMode.TopFieldFirst,
                _ => throw new InvalidDataException("Unexpected picture structure.")
            };
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
                    new(duration, new(1, 24)), default, presentation: H264Presentation.Create(header, nextOrder.Current, null));
                bool queued = false;
                try
                {
                    if (frame.Format.FullFrameHeight != header.Sequence.Height)
                    {
                        throw new InvalidDataException("The decoded surface lost the sequence's full frame height.");
                    }
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
            if (frame.Presentation is not VideoFramePresentation presentation || presentation.SourceScan != VideoSourceScan.Unknown)
            {
                throw new InvalidDataException("Field output must preserve its inferred presentation without declaring a source scan type.");
            }
            VideoFieldParity? leading = scan switch
            {
                VideoScanMode.Progressive => null,
                VideoScanMode.TopFieldFirst => VideoFieldParity.Top,
                VideoScanMode.BottomFieldFirst => VideoFieldParity.Bottom,
                _ => throw new ArgumentOutOfRangeException(nameof(scan))
            };
            if (leading is VideoFieldParity parity && presentation != VideoFramePresentation.Fields(parity, 2))
            {
                throw new InvalidDataException("Paired output has an incorrect field presentation sequence.");
            }
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

        internal static void VerifyPicture(H264Picture picture, ReadOnlySpan<byte> expected, int packet = 0)
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

        internal static async Task<byte[]> ReadReferenceAsync(string root, string name, int frame, H264SequenceParameterSet sequence)
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
