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
            byte[] expected = await ReadReferenceAsync(root, name, packet / 2);
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
                int referenceOffset = 0;
                for (int plane = 0; plane < 3; plane++)
                {
                    int width = picture.Stride(plane);
                    int rows = picture.Height / picture.VerticalScale(plane);
                    for (int row = 0; row < rows; row++)
                    {
                        ReadOnlySpan<byte> actual = picture.ReadPlane(plane).Slice(row * width, width);
                        if (!actual.SequenceEqual(expected.AsSpan(referenceOffset + row * 2 * width, width)))
                        {
                            throw new InvalidDataException($"Field mismatch in plane {plane}, row {row}.");
                        }
                    }
                    referenceOffset += rows * 2 * width;
                }
                if (referenceOffset != expected.Length)
                {
                    throw new InvalidDataException("Unexpected field reference image dimensions.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Field reconstruction retained its buffers.");
            }
        }

        private static async Task<byte[]> ReadReferenceAsync(string root, string name, int frame)
        {
            await using FileStream input = File.OpenRead(Path.Combine(root, name + ".yuv.gz"));
            await using GZipStream archive = new(input, CompressionMode.Decompress);
            byte[] expected = new byte[96 * 96 * 3 / 2];
            for (int index = 0; index <= frame; index++)
            {
                await archive.ReadExactlyAsync(expected);
            }
            return expected;
        }
    }
}
