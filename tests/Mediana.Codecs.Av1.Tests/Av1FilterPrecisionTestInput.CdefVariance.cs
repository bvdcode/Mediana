// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;

namespace Mediana.Codecs.Av1.Tests
{
    internal static partial class Av1FilterPrecisionTestInput
    {
        internal static void VerifyCdefVariance(string root, int bitDepth)
        {
            using GZipStream source = new(File.OpenRead(Path.Combine(root, "cdef-variance-precision.bin.gz")), CompressionMode.Decompress);
            using BinaryReader reader = new(source);
            Require(reader.ReadBytes(8).AsSpan().SequenceEqual("AV1HCDVA"u8) && reader.ReadInt32() == 864);
            Span<ushort> pixels = stackalloc ushort[256];
            Span<ushort> output = stackalloc ushort[256];
            Span<ushort> scratch = stackalloc ushort[192];
            Span<byte> bytePixels = stackalloc byte[256];
            Span<byte> byteOutput = stackalloc byte[256];
            Span<byte> byteScratch = stackalloc byte[192];
            int verified = 0;
            for (int index = 0; index < 864; index++)
            {
                int depth = reader.ReadByte();
                int side = reader.ReadByte();
                int x = reader.ReadByte();
                int y = reader.ReadByte();
                int primary = reader.ReadByte();
                int secondary = reader.ReadByte();
                int damping = reader.ReadByte();
                int direction = reader.ReadByte();
                int variance = reader.ReadInt32();
                Read(reader, pixels);
                byte[] expected = reader.ReadBytes(32);
                if (depth != bitDepth) { continue; }
                scratch.Clear();
                for (int row = 0; row < side + 4; row++)
                {
                    int position = y + row - 2;
                    if ((uint)position < 16) { pixels.Slice(position * 16, 16).CopyTo(scratch.Slice(row * 16, 16)); }
                }
                pixels.CopyTo(output);
                Av1CdefKernel.Filter<ushort>(scratch, output, 16, 16, x, y, side, primary, secondary, damping, direction, depth, variance);
                if (!Digest(output).AsSpan().SequenceEqual(expected))
                {
                    throw new InvalidDataException($"AV1 CDEF luma-variance vector {index} differs at {depth} bits, variance {variance}, primary {primary}.");
                }
                if (depth == 8)
                {
                    Copy(pixels, bytePixels);
                    Copy(scratch, byteScratch);
                    bytePixels.CopyTo(byteOutput);
                    Av1CdefKernel.Filter<byte>(byteScratch, byteOutput, 16, 16, x, y, side, primary, secondary, damping, direction, depth, variance);
                    Equal(output, byteOutput);
                }
                verified++;
            }
            Require(verified == 288 && source.ReadByte() == -1);
        }
    }
}
