// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Av1.Tests
{
    internal static class Av1GrainKernelTestInput
    {
        internal static void Verify(string root)
        {
            using FileStream input = File.OpenRead(Path.Combine(root, "grain-kernels.gz"));
            using GZipStream decompressed = new(input, CompressionMode.Decompress);
            using BinaryReader reader = new(decompressed);
            Require(reader.ReadInt32() == 0x324e5247);
            int count = reader.ReadInt32();
            long samples = 0;
            for (int index = 0; index < count; index++)
            {
                int width = reader.ReadInt32(), height = reader.ReadInt32(), depth = reader.ReadInt32();
                bool monochrome = reader.ReadInt32() != 0, identity = reader.ReadInt32() != 0;
                ushort seed = checked((ushort)reader.ReadInt32());
                Av1GrainPoint[] y = Points(reader), u = Points(reader), v = Points(reader);
                int scalingShift = reader.ReadInt32(), lag = reader.ReadInt32();
                sbyte[] yc = Coefficients(reader), uc = Coefficients(reader), vc = Coefficients(reader);
                int coefficientShift = reader.ReadInt32(), grainShift = reader.ReadInt32();
                Av1GrainChannel cb = Channel(reader, u, uc), cr = Channel(reader, v, vc);
                bool overlap = reader.ReadInt32() != 0, restricted = reader.ReadInt32() != 0, fromLuma = reader.ReadInt32() != 0;
                Av1GrainParameters parameters = new(seed, y, yc, cb, cr, fromLuma, scalingShift, lag, coefficientShift, grainShift, overlap, restricted);
                int length = reader.ReadInt32();
                byte[] expectedHash = reader.ReadBytes(SHA256.HashSizeInBytes);
                int sampleSize = depth == 8 ? 1 : 2;
                int chromaWidth = (width + 1) >> 1, chromaHeight = (height + 1) >> 1;
                PlaneLayout[] layout = monochrome ? [new(0, width * sampleSize, width * sampleSize, height)] :
                    [new(0, width * sampleSize, width * sampleSize, height),
                     new(width * height * sampleSize, chromaWidth * sampleSize, chromaWidth * sampleSize, chromaHeight),
                     new((width * height + chromaWidth * chromaHeight) * sampleSize, chromaWidth * sampleSize, chromaWidth * sampleSize, chromaHeight)];
                byte[] source = Source(index, width, height, depth, layout, length);
                byte[] actual = new byte[length];
                MediaBufferAllocator allocator = new(1024 * 1024);
                if (depth == 8)
                {
                    Av1GrainSynthesis.Apply(parameters, width, height, monochrome, identity, source, layout, actual, layout, allocator, default);
                }
                else
                {
                    ushort[] values = new ushort[length / 2], output = new ushort[length / 2];
                    for (int i = 0; i < values.Length; i++) { values[i] = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(i * 2)); }
                    Av1GrainSynthesis.Apply(parameters, width, height, depth, monochrome, identity, values, layout, output, layout, allocator, default);
                    for (int i = 0; i < output.Length; i++) { BinaryPrimitives.WriteUInt16LittleEndian(actual.AsSpan(i * 2), output[i]); }
                }
                if (!SHA256.HashData(actual).AsSpan().SequenceEqual(expectedHash))
                {
                    throw new InvalidDataException($"Film-grain synthesis differs from the independent reference at case {index}.");
                }
                Require(allocator.AllocatedBytes == 0);
                samples += length / sampleSize;
            }
            Require(decompressed.ReadByte() == -1 && count == 192 && samples == 277904);
        }

        private static byte[] Source(int test, int width, int height, int depth, PlaneLayout[] planes, int length)
        {
            byte[] source = new byte[length];
            int bytes = depth == 8 ? 1 : 2;
            for (int plane = 0; plane < planes.Length; plane++)
            {
                PlaneLayout layout = planes[plane];
                int columns = plane == 0 ? width : (width + 1) >> 1;
                int rows = plane == 0 ? height : (height + 1) >> 1;
                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        int value = ((column * 53 + row * 97 + plane * 131 + test * 17) ^ (column * row * 7 + test * 13)) & ((1 << depth) - 1);
                        int offset = layout.Offset + row * layout.Stride + column * bytes;
                        if (bytes == 1) { source[offset] = (byte)value; }
                        else { BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(offset), (ushort)value); }
                    }
                }
            }
            return source;
        }

        private static Av1GrainPoint[] Points(BinaryReader reader)
        {
            Av1GrainPoint[] result = new Av1GrainPoint[reader.ReadInt32()];
            for (int index = 0; index < result.Length; index++) { result[index] = new(checked((byte)reader.ReadInt32()), checked((byte)reader.ReadInt32())); }
            return result;
        }

        private static sbyte[] Coefficients(BinaryReader reader)
        {
            sbyte[] values = new sbyte[reader.ReadInt32()];
            for (int index = 0; index < values.Length; index++) { values[index] = checked((sbyte)reader.ReadInt32()); }
            return values;
        }

        private static Av1GrainChannel Channel(BinaryReader reader, Av1GrainPoint[] points, sbyte[] coefficients) =>
            new(points, coefficients, reader.ReadInt32() - 128, reader.ReadInt32() - 128, reader.ReadInt32() - 256);

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("The film-grain reference data or storage ownership is invalid."); }
        }
    }
}
