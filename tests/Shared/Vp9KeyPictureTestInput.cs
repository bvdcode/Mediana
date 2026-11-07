// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;

using Mediana.Codecs.Vp9;

namespace Mediana.Tests
{
    internal static class Vp9KeyPictureTestInput
    {
        internal static async Task VerifyAsync(string root, string name, bool filtered)
        {
            byte[] ivf = await File.ReadAllBytesAsync(Path.Combine(root, name + ".ivf"));
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + (filtered ? "-opening-filtered.yuv" : "-opening.yuv")));
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(ivf.AsSpan(32)));
            ReadOnlyMemory<byte> encoded = ivf.AsMemory(44, length); Vp9HeaderState state = new();
            Vp9FrameHeader header = Vp9FrameHeader.Parse(encoded.Span, state); if (!header.KeyFrame) { throw new InvalidDataException("The VP9 picture fixture must open with a keyframe."); }
            Vp9ProbabilityState probabilities = new(); probabilities.Begin(header);
            Vp9CompressedHeader compressed = Vp9CompressedHeader.Parse(encoded.Span.Slice(header.CompressedOffset, header.CompressedLength), header, probabilities.Working);
            if (header.Geometry.BitDepth == 8) { Verify<byte>(encoded, expected, header, state, compressed, probabilities.Working, filtered); }
            else { Verify<ushort>(encoded, expected, header, state, compressed, probabilities.Working, filtered); }
        }

        private static void Verify<T>(ReadOnlyMemory<byte> encoded, byte[] expected, Vp9FrameHeader header,
            Vp9HeaderState state, Vp9CompressedHeader compressed, Vp9ProbabilityContext probabilities, bool filtered) where T : unmanaged, IBinaryInteger<T>
        {
            int stride = header.SuperblockColumns * 64, height = header.SuperblockRows * 64;
            int subX = header.Geometry.SubsamplingX ? 1 : 0, subY = header.Geometry.SubsamplingY ? 1 : 0;
            int yLength = stride * height, uvLength = yLength >> (subX + subY), sampleBytes = Unsafe.SizeOf<T>();
            MediaBufferAllocator allocator = new(64 * 1024 * 1024, MemoryPool<byte>.Shared);
            using (WritableMediaBuffer output = allocator.Allocate((yLength + 2 * uvLength) * sampleBytes))
            {
                using (MediaBufferWorkspace workspace = new(allocator))
                {
                    Vp9ImageDecoder<T> decoder = new(header, state, compressed, probabilities, output.Span, workspace, null, default);
                    decoder.Decode(encoded[(header.CompressedOffset + header.CompressedLength)..]);
                    if (filtered) { decoder.Filter(); }
                }
                ReadOnlySpan<T> actual = MemoryMarshal.Cast<byte, T>(output.Span); int offset = 0;
                for (int plane = 0; plane < 3; plane++)
                {
                    int sx = plane == 0 ? 0 : subX, sy = plane == 0 ? 0 : subY;
                    int width = (header.Geometry.Width + ((1 << sx) - 1)) >> sx, rows = (header.Geometry.Height + ((1 << sy) - 1)) >> sy;
                    int planeOffset = plane switch { 0 => 0, 1 => yLength, 2 => yLength + uvLength, _ => throw new InvalidDataException() };
                    for (int y = 0; y < rows; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int reference = sampleBytes == 1 ? expected[offset] : BinaryPrimitives.ReadUInt16LittleEndian(expected.AsSpan(offset)); offset += sampleBytes;
                            int value = int.CreateTruncating(actual[planeOffset + y * (stride >> sx) + x]);
                            if (reference != value) { throw new InvalidDataException($"VP9 plane {plane}, ({x},{y}): expected {reference}, actual {value}."); }
                        }
                    }
                }
                if (expected.Length != offset) { throw new InvalidDataException("The VP9 picture reference extent differs."); }
            }
            if (allocator.AllocatedBytes != 0) { throw new InvalidOperationException("VP9 picture storage was not released."); }
        }
    }
}
