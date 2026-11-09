// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Numerics;

namespace Mediana.Codecs.Av1.Tests
{
    internal static partial class Av1FilterPrecisionTestInput
    {
        internal static void VerifyRestoration(string root, int bitDepth)
        {
            using GZipStream source = new(File.OpenRead(Path.Combine(root, "restoration-precision.bin.gz")), CompressionMode.Decompress);
            using BinaryReader reader = new(source);
            Require(reader.ReadBytes(8).AsSpan().SequenceEqual("AV1HLRES"u8) && reader.ReadInt32() == 6144);
            Span<ushort> pixels = stackalloc ushort[256];
            Span<ushort> output = stackalloc ushort[256];
            Span<ushort> scratch = stackalloc ushort[160];
            Span<byte> bytePixels = stackalloc byte[256];
            Span<byte> byteOutput = stackalloc byte[256];
            Span<byte> byteScratch = stackalloc byte[160];
            int verified = 0;
            for (int index = 0; index < 6144; index++)
            {
                int depth = reader.ReadByte();
                int kind = reader.ReadByte();
                int width = reader.ReadByte();
                int height = reader.ReadByte();
                int x = reader.ReadByte();
                int y = reader.ReadByte();
                Av1RestorationUnit unit = new()
                {
                    V0 = reader.ReadSByte(),
                    V1 = reader.ReadSByte(),
                    V2 = reader.ReadSByte(),
                    H0 = reader.ReadSByte(),
                    H1 = reader.ReadSByte(),
                    H2 = reader.ReadSByte(),
                    Set = reader.ReadByte(),
                    X0 = reader.ReadSByte(),
                    X1 = reader.ReadSByte()
                };
                Read(reader, pixels);
                byte[] expected = reader.ReadBytes(32);
                if (depth != bitDepth) { continue; }
                for (int row = 0; row < height + 6; row++)
                {
                    int position = Math.Clamp(y + row - 3, 0, 15);
                    pixels.Slice(position * 16, 16).CopyTo(scratch.Slice(row * 16, 16));
                }
                Av1RestorationSource<ushort> input = new(scratch, default, 16, 16, 16, y, 0, 64);
                pixels.CopyTo(output);
                Restore(input, output, kind, x, y, width, height, unit, depth);
                if (!Digest(output).AsSpan().SequenceEqual(expected))
                {
                    throw new InvalidDataException($"AV1 restoration vector {index} differs at {depth} bits, kind {kind}, {width}x{height} at {x},{y}.");
                }
                if (depth == 8)
                {
                    Copy(pixels, bytePixels);
                    Copy(scratch[..((height + 6) * 16)], byteScratch);
                    Av1RestorationSource<byte> byteInput = new(byteScratch, default, 16, 16, 16, y, 0, 64);
                    bytePixels.CopyTo(byteOutput);
                    Restore(byteInput, byteOutput, kind, x, y, width, height, unit, depth);
                    Equal(output, byteOutput);
                    bytePixels.CopyTo(byteOutput);
                    long byteBefore = GC.GetAllocatedBytesForCurrentThread();
                    Restore(byteInput, byteOutput, kind, x, y, width, height, unit, depth);
                    Require(GC.GetAllocatedBytesForCurrentThread() == byteBefore);
                }
                pixels.CopyTo(output);
                long before = GC.GetAllocatedBytesForCurrentThread();
                Restore(input, output, kind, x, y, width, height, unit, depth);
                Require(GC.GetAllocatedBytesForCurrentThread() == before);
                verified++;
            }
            Require(verified == 2048 && source.ReadByte() == -1);
        }

        private static void Restore<T>(Av1RestorationSource<T> input, Span<T> output, int kind, int x, int y,
            int width, int height, Av1RestorationUnit unit, int depth) where T : unmanaged, IBinaryInteger<T>
        {
            switch (kind)
            {
                case 0: Av1RestorationWiener.Filter(input, output, 16, x, y, width, height, unit, depth); break;
                case 1: Av1RestorationSelfGuided.Filter(input, output, 16, x, y, width, height, unit, depth); break;
                default: throw new InvalidDataException("The AV1 restoration fixture kind is invalid.");
            }
        }
    }
}
