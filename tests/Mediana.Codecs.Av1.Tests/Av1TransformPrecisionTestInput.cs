// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Mediana.Codecs.Av1.Tests
{
    internal static class Av1TransformPrecisionTestInput
    {
        private const long AllocationRegionBytes = 64 * 1024 * 1024;

        internal static void VerifyOneDimension(string root, int precision) => Verify(root, "transform1d-precision.bin.gz", 0, precision, 5796, 1932);
        internal static void VerifyTwoDimensions(string root, int bitDepth) => Verify(root, "transform2d-precision.bin.gz", 1, bitDepth, 3474, 1158);
        internal static void VerifyLossless(string root, int bitDepth) => Verify(root, "lossless-precision.bin.gz", 2, bitDepth, 291, 97);

        private static void Verify(string root, string fixture, int kind, int precision, int total, int expectedCount)
        {
            using GZipStream source = new(File.OpenRead(Path.Combine(root, fixture)), CompressionMode.Decompress);
            using BinaryReader reader = new(source);
            ReadOnlySpan<byte> magic = kind switch
            {
                0 => "AV1HTX1D"u8,
                1 => "AV1HTX2D"u8,
                2 => "AV1HWHT1"u8,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            Require(reader.ReadBytes(8).AsSpan().SequenceEqual(magic) && reader.ReadInt32() == total);
            if (!GC.TryStartNoGCRegion(AllocationRegionBytes))
            {
                throw new InvalidOperationException("The transform allocation interval could not be established.");
            }
            try
            {
                VerifyVectors(reader, kind, precision, total, expectedCount);
                long before = GC.GetAllocatedBytesForCurrentThread();
                byte[] control = new byte[37];
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                GC.KeepAlive(control);
                Require(allocated == (IntPtr.Size == 8 ? 64 : 52));
            }
            finally { GC.EndNoGCRegion(); }
            Require(source.ReadByte() == -1);
        }

        private static void VerifyVectors(BinaryReader reader, int kind, int precision, int total, int expectedCount)
        {
            Span<int> input = stackalloc int[4096];
            Span<int> actual = stackalloc int[4096];
            int verified = 0;
            for (int index = 0; index < total; index++)
            {
                int samplePrecision = reader.ReadByte();
                int type = kind == 2 ? 0 : reader.ReadByte();
                int width = kind == 2 ? 4 : reader.ReadByte();
                int height = kind switch
                {
                    0 => 1,
                    1 => reader.ReadByte(),
                    2 => 4,
                    _ => throw new ArgumentOutOfRangeException(nameof(kind))
                };
                int length = width * height;
                for (int coefficient = 0; coefficient < length; coefficient++) { input[coefficient] = reader.ReadInt32(); }
                byte[] expected = reader.ReadBytes(32);
                if (samplePrecision != precision) { continue; }
                Span<int> values = actual[..length];
                input[..length].CopyTo(values);
                Transform(values, kind, type, width, height, precision);
                if (!Digest(values).AsSpan().SequenceEqual(expected))
                {
                    throw new InvalidDataException($"AV1 transform vector {index} differs at {precision}-bit precision, kind {kind}, {width}x{height}, type {type}.");
                }
                input[..length].CopyTo(values);
                long before = GC.GetAllocatedBytesForCurrentThread();
                Transform(values, kind, type, width, height, precision);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                if (allocated != 0)
                {
                    throw new InvalidDataException($"AV1 transform vector {index} allocated {allocated} bytes at {precision}-bit precision, kind {kind}.");
                }
                verified++;
            }
            Require(verified == expectedCount);
        }

        private static void Transform(Span<int> values, int kind, int type, int width, int height, int precision)
        {
            switch (kind)
            {
                case 0: Av1InverseTransform.Transform(values, type, precision); break;
                case 1: Av1InverseTransform.Inverse(values, width, height, type, precision); break;
                case 2: Av1LosslessTransform.Inverse(values, precision); break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static byte[] Digest(ReadOnlySpan<int> values)
        {
            byte[] encoded = new byte[values.Length * 4];
            for (int index = 0; index < values.Length; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(encoded.AsSpan(index * 4), values[index]);
            }
            return SHA256.HashData(encoded);
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("AV1 transform precision fixture framing or count is invalid."); }
        }
    }
}
