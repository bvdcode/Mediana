// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Codecs.Hevc;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class HevcMotionCompensationTestInput
    {
        public static async Task<int> VerifyAsync(string root, bool measureAllocations = false)
        {
            SequenceParameters main = await HevcPictureStorageTestInput.ReadSequenceAsync(root, "main8");
            SequenceParameters main10 = await HevcPictureStorageTestInput.ReadSequenceAsync(root, "main10");
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (HevcMotionCompensator compensator = new(allocator))
            using (GZipStream file = new(File.OpenRead(Path.Combine(root, "motion.bin.gz")), CompressionMode.Decompress))
            using (BinaryReader input = new(file))
            {
                if (!input.ReadBytes(8).AsSpan().SequenceEqual("HEVCMCP1"u8)) { throw new InvalidDataException("Invalid HEVC motion reference."); }
                int count = input.ReadInt32();
                for (int test = 0; test < count; test++)
                {
                    int depth = input.ReadInt32(), x = input.ReadInt32(), y = input.ReadInt32(), width = input.ReadInt32(), height = input.ReadInt32();
                    HevcMotionVector motion0 = new((short)input.ReadInt32(), (short)input.ReadInt32());
                    HevcMotionVector motion1 = new((short)input.ReadInt32(), (short)input.ReadInt32());
                    int direction = input.ReadInt32(), weighted = input.ReadInt32(), denominator = input.ReadInt32();
                    HevcPredictionWeight weight0 = ReadWeight(input), weight1 = ReadWeight(input);
                    HevcPredictionWeights? weights = weighted == 0 ? null : new(denominator, denominator, [weight0], [weight1]);
                    SequenceParameters sequence = depth switch
                    {
                        8 => main,
                        10 => main10,
                        _ => throw new InvalidDataException("Invalid HEVC motion precision.")
                    };
                    using DecodedPicture first = new(sequence, allocator), second = new(sequence, allocator), target = new(sequence, allocator);
                    SamplePlane[] output = [target.Luma, target.Cb!, target.Cr!];
                    SamplePlane[] source0 = [first.Luma, first.Cb!, first.Cr!], source1 = [second.Luma, second.Cb!, second.Cr!];
                    int[][] expected = new int[3][];
                    for (int component = 0; component < 3; component++)
                    {
                        Fill(input, source0[component]); Fill(input, source1[component]);
                        int scale = component == 0 ? 1 : 2;
                        expected[component] = new int[(width / scale) * (height / scale)];
                        for (int i = 0; i < expected[component].Length; i++) { expected[component][i] = input.ReadInt32(); }
                    }
                    using MediaBuffer published0 = first.PublishPixels(), published1 = second.PublishPixels();
                    HevcPredictionReference? reference0 = null, reference1 = null;
                    switch (direction)
                    {
                        case 0: reference0 = new(first, motion0, 0); break;
                        case 1: reference1 = new(second, motion1, 0); break;
                        case 2: reference0 = new(first, motion0, 0); reference1 = new(second, motion1, 0); break;
                        default: throw new InvalidDataException("Invalid HEVC prediction direction.");
                    }
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    compensator.Predict(target, x, y, width, height, reference0, reference1, weights);
                    if (measureAllocations && GC.GetAllocatedBytesForCurrentThread() != before)
                    { throw new InvalidDataException("HEVC motion compensation allocated per-block storage."); }
                    for (int component = 0; component < 3; component++)
                    {
                        int scale = component == 0 ? 1 : 2;
                        Verify(output[component], x / scale, y / scale, width / scale, height / scale, expected[component], test);
                    }
                }
                if (file.ReadByte() != -1) { throw new InvalidDataException("The HEVC motion reference has trailing data."); }
                if (count != 768) { throw new InvalidDataException("Incomplete HEVC motion corpus."); }
            }
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("HEVC motion compensation retained pixel or working storage."); }
            return 768;
        }

        private static HevcPredictionWeight ReadWeight(BinaryReader reader) => new(reader.ReadInt32(), reader.ReadInt32(),
            reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

        private static void Fill(BinaryReader input, SamplePlane plane)
        {
            for (int i = 0; i < plane.Width * plane.Height; i++)
            {
                ushort sample = input.ReadUInt16();
                switch (plane.BitDepth)
                {
                    case 8: plane.ByteView.Samples[i] = checked((byte)sample); break;
                    case 10: plane.WordView.Samples[i] = sample; break;
                    default: throw new InvalidDataException("Invalid component precision.");
                }
            }
        }

        private static void Verify(SamplePlane plane, int x, int y, int width, int height, ReadOnlySpan<int> expected, int test)
        {
            for (int row = 0; row < plane.Height; row++)
            {
                for (int column = 0; column < plane.Width; column++)
                {
                    int actual = plane.BitDepth switch
                    {
                        8 => plane.ReadByteView.Row(row)[column],
                        10 => plane.ReadWordView.Row(row)[column],
                        _ => throw new InvalidDataException("Invalid component precision.")
                    };
                    int wanted = column >= x && column < x + width && row >= y && row < y + height
                        ? expected[(row - y) * width + column - x] : 0;
                    if (actual != wanted)
                    { throw new InvalidDataException($"HEVC motion {test} at {column},{row}: {actual}, expected {wanted}."); }
                }
            }
        }
    }
}
