// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;

namespace Mediana.Codecs.Av1.Tests
{
    internal static partial class Av1FilterPrecisionTestInput
    {
        internal static void VerifyCdef(string root, int bitDepth)
        {
            using GZipStream source = new(File.OpenRead(Path.Combine(root, "cdef-precision.bin.gz")), CompressionMode.Decompress);
            using BinaryReader reader = new(source);
            Require(reader.ReadBytes(8).AsSpan().SequenceEqual("AV1HCDEF"u8) && reader.ReadInt32() == 3072);
            Span<ushort> pixels = stackalloc ushort[256];
            Span<ushort> output = stackalloc ushort[256];
            Span<ushort> scratch = stackalloc ushort[192];
            Span<byte> bytePixels = stackalloc byte[256];
            Span<byte> byteOutput = stackalloc byte[256];
            Span<byte> byteScratch = stackalloc byte[192];
            int verified = 0;
            for (int index = 0; index < 3072; index++)
            {
                int depth = reader.ReadByte();
                int side = reader.ReadByte();
                int x = reader.ReadByte();
                int y = reader.ReadByte();
                int primary = reader.ReadByte();
                int secondary = reader.ReadByte();
                int damping = reader.ReadByte();
                int direction = reader.ReadByte();
                int expectedDirection = reader.ReadByte();
                int expectedVariance = reader.ReadInt32();
                Read(reader, pixels);
                byte[] expected = reader.ReadBytes(32);
                if (depth != bitDepth) { continue; }
                int actualDirection = Av1CdefDirection.Find<ushort>(pixels[68..], 16, out int variance, depth);
                Require(actualDirection == expectedDirection && variance == expectedVariance);
                scratch.Clear();
                for (int row = 0; row < side + 4; row++)
                {
                    int position = y + row - 2;
                    if ((uint)position < 16) { pixels.Slice(position * 16, 16).CopyTo(scratch.Slice(row * 16, 16)); }
                }
                pixels.CopyTo(output);
                Av1CdefKernel.Filter<ushort>(scratch, output, 16, 16, x, y, side, primary, secondary, damping, direction, depth);
                if (!Digest(output).AsSpan().SequenceEqual(expected))
                {
                    throw new InvalidDataException($"AV1 CDEF vector {index} differs at {depth} bits, side {side}, direction {direction}.");
                }
                if (depth == 8)
                {
                    Copy(pixels, bytePixels);
                    Copy(scratch, byteScratch);
                    bytePixels.CopyTo(byteOutput);
                    Require(Av1CdefDirection.Find<byte>(bytePixels[68..], 16, out int byteVariance) == expectedDirection && byteVariance == expectedVariance);
                    Av1CdefKernel.Filter<byte>(byteScratch, byteOutput, 16, 16, x, y, side, primary, secondary, damping, direction);
                    Equal(output, byteOutput);
                    bytePixels.CopyTo(byteOutput);
                    long byteBefore = GC.GetAllocatedBytesForCurrentThread();
                    _ = Av1CdefDirection.Find<byte>(bytePixels[68..], 16, out _);
                    Av1CdefKernel.Filter<byte>(byteScratch, byteOutput, 16, 16, x, y, side, primary, secondary, damping, direction);
                    Require(GC.GetAllocatedBytesForCurrentThread() == byteBefore);
                }
                pixels.CopyTo(output);
                long before = GC.GetAllocatedBytesForCurrentThread();
                _ = Av1CdefDirection.Find<ushort>(pixels[68..], 16, out _, depth);
                Av1CdefKernel.Filter<ushort>(scratch, output, 16, 16, x, y, side, primary, secondary, damping, direction, depth);
                Require(GC.GetAllocatedBytesForCurrentThread() == before);
                verified++;
            }
            Require(verified == 1024 && source.ReadByte() == -1);
        }

        internal static void VerifyDeblocking(string root, int bitDepth)
        {
            using GZipStream source = new(File.OpenRead(Path.Combine(root, "loopfilter-precision.bin.gz")), CompressionMode.Decompress);
            using BinaryReader reader = new(source);
            Require(reader.ReadBytes(8).AsSpan().SequenceEqual("AV1HLPF1"u8) && reader.ReadInt32() == 6144);
            Span<ushort> pixels = stackalloc ushort[256];
            Span<ushort> output = stackalloc ushort[256];
            Span<byte> bytePixels = stackalloc byte[256];
            Span<byte> byteOutput = stackalloc byte[256];
            int verified = 0;
            for (int index = 0; index < 6144; index++)
            {
                int depth = reader.ReadByte();
                int kind = reader.ReadByte();
                int direction = reader.ReadByte();
                int limit = reader.ReadByte();
                int boundary = reader.ReadByte();
                int threshold = reader.ReadByte();
                Read(reader, pixels);
                byte[] expected = reader.ReadBytes(32);
                if (depth != bitDepth) { continue; }
                int size = kind switch
                {
                    4 => 4,
                    6 or 8 => 8,
                    14 => 16,
                    _ => throw new InvalidDataException("The AV1 deblocking fixture size is invalid.")
                };
                pixels.CopyTo(output);
                Deblock(output, size, kind == 6, direction, limit, boundary, threshold, depth);
                if (!Digest(output).AsSpan().SequenceEqual(expected))
                {
                    throw new InvalidDataException($"AV1 deblocking vector {index} differs at {depth} bits, size {kind}, direction {direction}.");
                }
                if (depth == 8)
                {
                    Copy(pixels, bytePixels);
                    bytePixels.CopyTo(byteOutput);
                    Deblock(byteOutput, size, kind == 6, direction, limit, boundary, threshold, depth);
                    Equal(output, byteOutput);
                    bytePixels.CopyTo(byteOutput);
                    long byteBefore = GC.GetAllocatedBytesForCurrentThread();
                    Deblock(byteOutput, size, kind == 6, direction, limit, boundary, threshold, depth);
                    Require(GC.GetAllocatedBytesForCurrentThread() == byteBefore);
                }
                pixels.CopyTo(output);
                long before = GC.GetAllocatedBytesForCurrentThread();
                Deblock(output, size, kind == 6, direction, limit, boundary, threshold, depth);
                Require(GC.GetAllocatedBytesForCurrentThread() == before);
                verified++;
            }
            Require(verified == 2048 && source.ReadByte() == -1);
        }

        private static void Deblock<T>(Span<T> pixels, int size, bool chroma, int direction, int limit, int boundary, int threshold, int depth) where T : unmanaged, IBinaryInteger<T>
        {
            (int across, int along) = direction switch
            {
                0 => (1, 16),
                1 => (16, 1),
                _ => throw new InvalidDataException("The AV1 deblocking fixture direction is invalid.")
            };
            for (int lane = 0; lane < 4; lane++)
            {
                Av1LoopFilterKernel.Apply<T>(pixels, 136 + lane * along, across, size, chroma, limit, boundary, threshold, depth);
            }
        }

        private static void Copy(ReadOnlySpan<ushort> source, Span<byte> destination)
        {
            for (int index = 0; index < source.Length; index++) { destination[index] = checked((byte)source[index]); }
        }

        private static void Equal(ReadOnlySpan<ushort> expected, ReadOnlySpan<byte> actual)
        {
            for (int index = 0; index < expected.Length; index++) { Require(expected[index] == actual[index]); }
        }

        private static void Read(BinaryReader reader, Span<ushort> pixels)
        {
            for (int index = 0; index < pixels.Length; index++) { pixels[index] = reader.ReadUInt16(); }
        }

        private static byte[] Digest(ReadOnlySpan<ushort> pixels)
        {
            byte[] encoded = new byte[pixels.Length * 2];
            for (int index = 0; index < pixels.Length; index++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(encoded.AsSpan(index * 2), pixels[index]);
            }
            return SHA256.HashData(encoded);
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("The AV1 filter precision framing, direction, variance or allocation differs from its reference."); }
        }
    }
}
