// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Mediana.Codecs.Av1.Tests
{
    internal static class Av1HighDepthPredictionTestInput
    {
        internal static void Verify(string root, int bitDepth)
        {
            using GZipStream source = new(File.OpenRead(Path.Combine(root, "intra-high-depth.bin.gz")), CompressionMode.Decompress);
            using BinaryReader reader = new(source);
            Require(reader.ReadBytes(8).AsSpan().SequenceEqual("AV1HIN01"u8));
            int count = reader.ReadInt32();
            Require(count == 2336);
            int verified = 0;
            for (int index = 0; index < count; index++)
            {
                int depth = reader.ReadByte();
                int kind = reader.ReadByte();
                int width = reader.ReadByte();
                int height = reader.ReadByte();
                int mode = reader.ReadByte();
                ushort[] above = Read(reader, width + 1);
                ushort[] left = Read(reader, height + 1);
                byte[] full = reader.ReadBytes(32);
                byte[] clipped = reader.ReadBytes(32);
                if (depth != bitDepth) { continue; }
                ushort[] output = new ushort[width * height];
                Predict(output, kind, width, height, width, height, above, left, mode, depth);
                Require(Digest(output, width, height, width).AsSpan().SequenceEqual(full));
                output.AsSpan().Fill(ushort.MaxValue);
                Predict(output, kind, width, height, width - 1, height - 1, above, left, mode, depth);
                Require(Digest(output, width - 1, height - 1, width).AsSpan().SequenceEqual(clipped));
                for (int row = 0; row < height; row++)
                {
                    for (int column = 0; column < width; column++)
                    {
                        if (row == height - 1 || column == width - 1) { Require(output[row * width + column] == ushort.MaxValue); }
                    }
                }
                long before = GC.GetAllocatedBytesForCurrentThread();
                Predict(output, kind, width, height, width, height, above, left, mode, depth);
                Require(GC.GetAllocatedBytesForCurrentThread() == before);
                verified++;
            }
            Require(verified == 1168 && source.ReadByte() == -1);
        }

        internal static void VerifyDirections(string root, int bitDepth)
        {
            using GZipStream source = new(File.OpenRead(Path.Combine(root, "directional-high-depth.bin.gz")), CompressionMode.Decompress);
            using BinaryReader reader = new(source);
            Require(reader.ReadBytes(8).AsSpan().SequenceEqual("AV1HDIR1"u8));
            int count = reader.ReadInt32();
            Require(count == 8512);
            int verified = 0;
            for (int index = 0; index < count; index++)
            {
                int depth = reader.ReadByte();
                int width = reader.ReadByte();
                int height = reader.ReadByte();
                int angle = reader.ReadByte();
                bool smooth = reader.ReadByte() != 0;
                int available = reader.ReadByte();
                int visibleWidth = reader.ReadByte();
                int visibleHeight = reader.ReadByte();
                int edgeCount = width + height;
                ushort[] above = new ushort[edgeCount * 2 + 2];
                ushort[] left = new ushort[above.Length];
                Read(reader, edgeCount + 2).CopyTo(above, 0);
                Read(reader, edgeCount + 2).CopyTo(left, 0);
                byte[] expected = reader.ReadBytes(32);
                if (depth != bitDepth) { continue; }
                ushort[] output = new ushort[width * height];
                output.AsSpan().Fill(ushort.MaxValue);
                Av1IntraEdges<ushort> edges = new(above, left, visibleWidth, visibleHeight, (available & 1) != 0, (available & 2) != 0, smooth);
                Av1DirectionalPrediction.Predict(output, width, new(width, height), edges, angle, true, depth);
                Require(Digest(output, visibleWidth, visibleHeight, width).AsSpan().SequenceEqual(expected));
                for (int row = 0; row < height; row++)
                {
                    for (int column = 0; column < width; column++)
                    {
                        if (row >= visibleHeight || column >= visibleWidth) { Require(output[row * width + column] == ushort.MaxValue); }
                    }
                }
                long before = GC.GetAllocatedBytesForCurrentThread();
                Av1DirectionalPrediction.Predict(output, width, new(width, height), edges, angle, true, depth);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                if (allocated != 0)
                {
                    throw new InvalidDataException($"AV1 directional prediction allocated {allocated} bytes at vector {index}, {depth}-bit {width}x{height}, angle {angle}.");
                }
                verified++;
            }
            Require(verified == 4256 && source.ReadByte() == -1);
        }

        private static void Predict(Span<ushort> output, int kind, int width, int height, int visibleWidth, int visibleHeight,
            ReadOnlySpan<ushort> above, ReadOnlySpan<ushort> left, int mode, int bitDepth)
        {
            switch (kind)
            {
                case 0:
                    Av1IntraPredictor.Predict(output, width, width, height, visibleWidth, visibleHeight, above, left, mode);
                    break;
                case 1:
                    Av1FilterIntraPrediction.Predict(output, width, width, height, visibleWidth, visibleHeight, above, left, mode, bitDepth);
                    break;
                default:
                    throw new InvalidDataException("The AV1 high-depth prediction fixture kind is invalid.");
            }
        }

        private static ushort[] Read(BinaryReader reader, int count)
        {
            ushort[] result = new ushort[count];
            for (int index = 0; index < count; index++) { result[index] = reader.ReadUInt16(); }
            return result;
        }

        private static byte[] Digest(ReadOnlySpan<ushort> values, int width, int height, int stride)
        {
            byte[] encoded = new byte[width * height * 2];
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(encoded.AsSpan((row * width + column) * 2), values[row * stride + column]);
                }
            }
            return SHA256.HashData(encoded);
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("AV1 high-depth prediction samples, clipped writes or allocations differ from the reference."); }
        }
    }
}
