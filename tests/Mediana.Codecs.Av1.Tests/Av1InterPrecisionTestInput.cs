// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1.Tests
{
    internal static class Av1InterPrecisionTestInput
    {
        internal static int Verify(string root)
        {
            using FileStream compressed = File.OpenRead(Path.Combine(root, "inter-precision.bin.gz"));
            using GZipStream expanded = new(compressed, CompressionMode.Decompress);
            using BinaryReader reader = new(expanded);
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("AV1INTER"u8) || reader.ReadInt32() != 1890)
            { throw new InvalidDataException("Invalid inter prediction precision fixture header."); }
            MediaBufferAllocator allocator = new(1024 * 1024);
            int samples = 0;
            using (Av1PictureDecoder decoder = new(allocator))
            using (Av1Picture template = decoder.Decode(File.ReadAllBytes(Path.Combine(root, "yuvNoise32.obu"))))
            {
                samples += VerifyDepth<byte>(reader, template, allocator, 8);
                samples += VerifyDepth<ushort>(reader, template, allocator, 10);
                samples += VerifyDepth<ushort>(reader, template, allocator, 12);
            }
            if (allocator.AllocatedBytes != 0 || expanded.ReadByte() != -1)
            { throw new InvalidDataException("Inter prediction fixture leaked storage or has trailing data."); }
            return samples;
        }

        private static int VerifyDepth<T>(BinaryReader reader, Av1Picture template, MediaBufferAllocator allocator, int depth)
            where T : unmanaged, IBinaryInteger<T>
        {
            ushort[][] originals = [new ushort[11520], new ushort[11520]];
            foreach (ushort[] original in originals)
            {
                for (int index = 0; index < original.Length; index++) { original[index] = reader.ReadUInt16(); }
            }
            int samples = 0;
            int greatest = int.MinValue;
            int least = int.MaxValue;
            for (int index = 0; index < 630; index++)
            {
                if (reader.ReadInt32() != depth) { throw new InvalidDataException("Unexpected inter prediction precision."); }
                samples += VerifyCase<T>(reader, template, allocator, originals, depth, index, out int minimum, out int maximum);
                least = Math.Min(least, minimum);
                greatest = Math.Max(greatest, maximum);
            }
            if (least >= 0 || (depth > 8 && greatest <= short.MaxValue))
            { throw new InvalidDataException("The inter prediction fixture does not exercise signed intermediate extrema."); }
            return samples;
        }

        private static int VerifyCase<T>(BinaryReader reader, Av1Picture template, MediaBufferAllocator allocator,
            ushort[][] originals, int depth, int index, out int minimum, out int maximum) where T : unmanaged, IBinaryInteger<T>
        {
            int kind = reader.ReadInt32();
            int pattern = reader.ReadInt32();
            int plane = reader.ReadInt32();
            int targetWidth = reader.ReadInt32();
            int targetHeight = reader.ReadInt32();
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            int predictionWidth = reader.ReadInt32();
            int predictionHeight = reader.ReadInt32();
            int filterX = reader.ReadInt32();
            int filterY = reader.ReadInt32();
            int motionX = reader.ReadInt32();
            int motionY = reader.ReadInt32();
            int secondX = reader.ReadInt32();
            int secondY = reader.ReadInt32();
            int maskKind = reader.ReadInt32();
            int sign = reader.ReadInt32();
            int firstWeight = reader.ReadInt32();
            int secondWeight = reader.ReadInt32();
            minimum = reader.ReadInt32();
            maximum = reader.ReadInt32();
            int maskStride = reader.ReadInt32();
            int maskHeight = reader.ReadInt32();
            Av1WarpParameters firstWarp = ReadWarp(reader);
            Av1WarpParameters secondWarp = ReadWarp(reader);
            ushort[] expected = new ushort[width * height];
            for (int sample = 0; sample < expected.Length; sample++) { expected[sample] = reader.ReadUInt16(); }
            byte[] expectedMask = reader.ReadBytes(maskStride * maskHeight);
            byte[] mask = new byte[expectedMask.Length];
            for (int row = 0; row < maskHeight; row++)
            {
                for (int column = 0; column < maskStride; column++)
                { mask[row * maskStride + column] = (byte)((row * 17 + column * 11 + sign * 23) % 65); }
            }
            Av1SequenceHeader source = template.Sequence with { Width = 96, Height = 80 };
            Av1SequenceHeader target = source with { Width = targetWidth, Height = targetHeight };
            T[] firstPixels = Pixels<T>(source, depth, pattern, 0, originals[0]);
            T[] secondPixels = Pixels<T>(source, depth, pattern, 1, originals[1]);
            using WritableMediaBuffer firstWrite = allocator.Allocate(checked(firstPixels.Length * Unsafe.SizeOf<T>()));
            using WritableMediaBuffer secondWrite = allocator.Allocate(checked(secondPixels.Length * Unsafe.SizeOf<T>()));
            firstPixels.AsSpan().CopyTo(MemoryMarshal.Cast<byte, T>(firstWrite.Span));
            secondPixels.AsSpan().CopyTo(MemoryMarshal.Cast<byte, T>(secondWrite.Span));
            using WritableMediaBuffer probabilityWrite = allocator.Allocate(1);
            using MediaBuffer probabilities = probabilityWrite.Publish();
            using Av1Picture firstPicture = new(template.Header with { Geometry = source }, firstWrite.Publish());
            using Av1Picture secondPicture = new(template.Header with { Geometry = source }, secondWrite.Publish());
            using Av1StoredPicture first = new(firstPicture, probabilities);
            using Av1StoredPicture second = new(secondPicture, probabilities);
            T[] output = new T[target.SampleCount];
            T sentinel = T.CreateTruncating(65517);
            output.AsSpan().Fill(sentinel);
            Av1MotionBlock motion = new()
            {
                Row = motionY,
                Column = motionX,
                SecondRow = checked((short)secondY),
                SecondColumn = checked((short)secondX),
                FilterX = checked((byte)filterX),
                FilterY = checked((byte)filterY)
            };
            Predict();
            if (index % 64 == 0)
            {
                if (!GC.TryStartNoGCRegion(1024 * 1024))
                { throw new InvalidOperationException("The inter prediction allocation interval could not be established."); }
                long allocated;
                long controlAllocated;
                try
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int repetition = 0; repetition < 32; repetition++) { Predict(); }
                    allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    long controlBefore = GC.GetAllocatedBytesForCurrentThread();
                    byte[] control = new byte[37];
                    controlAllocated = GC.GetAllocatedBytesForCurrentThread() - controlBefore;
                    GC.KeepAlive(control);
                }
                finally { GC.EndNoGCRegion(); }
                if (allocated != 0 || controlAllocated != (IntPtr.Size == 8 ? 64 : 52))
                { throw new InvalidDataException($"Inter prediction allocated {allocated} bytes; allocation control was {controlAllocated} bytes."); }
            }
            int offset = target.PlaneOffset(plane);
            int stride = target.PlaneStride(plane);
            for (int sample = 0; sample < output.Length; sample++)
            {
                int local = sample - offset;
                int row = local / stride - y;
                int column = local % stride - x;
                bool inside = local >= 0 && row >= 0 && row < height && column >= 0 && column < width;
                int value = inside ? expected[row * width + column] : int.CreateTruncating(sentinel);
                if (int.CreateTruncating(output[sample]) != value)
                { throw new InvalidDataException($"Inter prediction mismatch: {depth}-bit case {index}, kind {kind}, sample {sample}, expected {value}, actual {output[sample]}."); }
            }
            if (!mask.AsSpan().SequenceEqual(expectedMask) ||
                !MemoryMarshal.Cast<byte, T>(first.Pixels.Memory.Span).SequenceEqual(firstPixels) ||
                !MemoryMarshal.Cast<byte, T>(second.Pixels.Memory.Span).SequenceEqual(secondPixels))
            { throw new InvalidDataException("Inter prediction modified its mask or reference outside the required result."); }
            return expected.Length;

            void Predict()
            {
                switch (kind)
                {
                    case 0:
                        Av1MotionCompensation.Apply<T>(output, target, first, plane, x, y, width, height, motion, predictionWidth, predictionHeight, depth);
                        break;
                    case 1:
                        Av1WarpPrediction.Apply<T>(output, target, first, plane, x, y, width, height, firstWarp, depth);
                        break;
                    case 2 or 3 or 4:
                        Av1CompoundMaskType maskType = maskKind switch
                        {
                            0 => Av1CompoundMaskType.None,
                            1 => Av1CompoundMaskType.Wedge,
                            2 => Av1CompoundMaskType.Difference,
                            _ => throw new InvalidDataException("Invalid inter prediction mask kind.")
                        };
                        Av1CompoundPrediction compound = new(new(firstWeight, secondWeight), maskType, Sign: sign);
                        Av1MotionCompensation.ApplyCompound<T>(output, target, first, second, plane, x, y, width, height,
                            motion, compound, predictionWidth, predictionHeight, mask, maskStride,
                            kind is 3 or 4 ? firstWarp : default, kind == 3 ? secondWarp : default, depth);
                        break;
                    default:
                        throw new InvalidDataException("Invalid inter prediction kind.");
                }
            }
        }

        private static T[] Pixels<T>(Av1SequenceHeader geometry, int depth, int pattern, int which, ushort[] original)
            where T : unmanaged, IBinaryInteger<T>
        {
            T[] result = new T[geometry.SampleCount];
            int maximum = (1 << depth) - 1;
            ReadOnlySpan<int> sharp = [-4, 12, -24, 80, 80, -24, 12, -4];
            for (int plane = 0; plane < 3; plane++)
            {
                int sub = geometry.PlaneShift(plane);
                int width = geometry.Width >> sub;
                int height = geometry.Height >> sub;
                int offset = geometry.PlaneOffset(plane);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int value;
                        switch (pattern)
                        {
                            case 0: value = original[offset + y * width + x]; break;
                            case 1: value = 0; break;
                            case 2: value = maximum; break;
                            case 3: value = ((x + y + which) & 1) != 0 ? maximum : 0; break;
                            case 4 or 5:
                                int a = x - (32 >> sub) + 3;
                                int b = y - (32 >> sub) + 3;
                                bool positive = a >= 0 && a < 8 && b >= 0 && b < 8 && sharp[a] * sharp[b] > 0;
                                if ((pattern == 5) ^ (which != 0)) { positive = !positive; }
                                value = positive ? maximum : 0;
                                break;
                            default: throw new InvalidDataException("Invalid inter prediction source pattern.");
                        }
                        result[offset + y * width + x] = T.CreateTruncating(value);
                    }
                }
            }
            return result;
        }

        private static Av1WarpParameters ReadWarp(BinaryReader reader) => Av1WarpParameters.Create(
            reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
    }
}
