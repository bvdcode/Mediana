// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Codecs.H264;
using Mediana.Core.Memory;

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

        public static async Task VerifySequenceAsync(string root, string name, IReadOnlyList<int>? referenceFrames = null)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264ReferenceBuffer references = new())
            {
                H264PictureOrderState order = new();
                for (int packet = 0; packet < 24; packet++)
                {
                    (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Path.Combine(root, name + ".mp4"), packet);
                    byte[] expected = await ReadReferenceAsync(root, name, referenceFrames is null ? packet / 2 : referenceFrames[packet / 2],
                        configuration.Sequences.Values.Single());
                    order = DecodeAndStore(configuration, nal, expected, order, references, allocator, packet);
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Field reference decoding retained its buffers.");
            }
        }

        private static H264PictureOrderState DecodeAndStore(H264Configuration configuration, ReadOnlySpan<byte> nal,
            ReadOnlySpan<byte> expected, H264PictureOrderState order, H264ReferenceBuffer references, MediaBufferAllocator allocator, int packet)
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
                references.Store(picture, header, nextOrder.Current);
                stored = true;
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

        private static void VerifyPicture(H264Picture picture, ReadOnlySpan<byte> expected, int packet = 0)
        {
            int parity = picture.Structure switch
            {
                H264PictureStructure.TopField => 0,
                H264PictureStructure.BottomField => 1,
                _ => throw new InvalidDataException("Expected a field picture.")
            };
            int referenceOffset = 0;
            for (int plane = 0; plane < 3; plane++)
            {
                int width = picture.Stride(plane) * picture.SampleBytes;
                int rows = picture.Height / picture.VerticalScale(plane);
                for (int row = 0; row < rows; row++)
                {
                    ReadOnlySpan<byte> actual = picture.ReadPlane(plane).Slice(row * width, width);
                    if (!actual.SequenceEqual(expected.Slice(referenceOffset + (row * 2 + parity) * width, width)))
                    {
                        throw new InvalidDataException($"Field mismatch in packet {packet}, plane {plane}, row {row}.");
                    }
                }
                referenceOffset += rows * 2 * width;
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
