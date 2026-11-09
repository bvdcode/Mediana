// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    internal static class H264MotionSeedingTestInput
    {
        public static void Verify()
        {
            MediaBufferAllocator allocator = new(1_000_000);
            int recovered = 0;
            for (int seed = 0; seed < 7; seed++)
            {
                foreach ((int x, int y) in new (int, int)[] { (8, 8), (-8, -8), (8, -8), (-8, 8) })
                {
                    recovered += Verify(allocator, seed, x, y);
                }
            }
            if (recovered == 0 || allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Motion seeds did not recover any local search minimum or retained storage.");
            }
        }

        private static int Verify(MediaBufferAllocator allocator, int seed, int dx, int dy)
        {
            H264SequenceParameterSet sequence = new()
            {
                WidthInMbs = 4,
                HeightInMapUnits = 4,
                FrameMbsOnly = true,
                Width = 64,
                Height = 64
            };
            using H264Picture reference = new(sequence, allocator, H264SliceKind.P);
            using H264Picture target = new(sequence, allocator, H264SliceKind.P);
            reference.SetMotion(H264ReferenceList.L0, 0, 0, 64, 64, 0, 0, 0);
            target.SetMotion(H264ReferenceList.L0, 0, 0, 64, 64, 0, 0, 0);
            (int sx, int sy) = seed switch
            {
                0 => (0, 16),
                1 => (16, 0),
                2 => (32, 0),
                3 => (0, 0),
                4 => (16, 16),
                5 => (32, 16),
                6 => (16, 32),
                _ => throw new ArgumentOutOfRangeException(nameof(seed))
            };
            H264Picture seeded = seed < 4 ? target : reference;
            seeded.SetMotion(H264ReferenceList.L0, sx, sy, 16, 16, 0, dx * 4, dy * 4);
            reference.Modes.Fill(H264Picture.InterMode);
            target.Modes.Fill(H264Picture.InterMode);
            Random random = new(1863);
            VideoFormat format = new(64, 64, VideoPixelFormat.Yuv420P8);
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(6144);
            for (int plane = 0; plane < 3; plane++)
            {
                Span<byte> pixels = reference.Plane(plane);
                random.NextBytes(pixels);
                int width = reference.Stride(plane);
                int scale = plane == 0 ? 1 : 2;
                for (int row = 0; row < width; row++)
                {
                    for (int column = 0; column < width; column++)
                    {
                        int x = Math.Clamp(column + dx / scale, 0, width - 1);
                        int y = Math.Clamp(row + dy / scale, 0, width - 1);
                        writable.Span[layout[plane].Offset + row * width + column] = pixels[y * width + x];
                    }
                }
            }
            using MediaBuffer storage = writable.Publish();
            using VideoFrame input = new(format, storage, layout);
            Span<byte> samples = stackalloc byte[384];
            Span<byte> prediction = stackalloc byte[390];
            prediction.Fill(183);
            H264SourceMacroblock source = new(input, 16, 16, samples);
            byte[] targetModes = target.Modes.ToArray();
            byte[] referenceModes = reference.Modes.ToArray();
            int[] targetX = target.MotionX(H264ReferenceList.L0).ToArray();
            int[] targetY = target.MotionY(H264ReferenceList.L0).ToArray();
            int[] referenceX = reference.MotionX(H264ReferenceList.L0).ToArray();
            int[] referenceY = reference.MotionY(H264ReferenceList.L0).ToArray();
            byte[] original = storage.Memory.ToArray();
            (int _, int _, bool oldExact) = H264MotionSearch.Select(source, reference, target, 16, 16, 16, prediction.Slice(3, 384), default);
            VerifyResult(source, null);
            using H264MotionReference prepared = new(reference, 16, allocator);
            prepared.Prepare(reference, default);
            VerifyResult(source, prepared);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 32; iteration++)
            {
                H264MotionSearch.Select<H264DetailedMacroblockAnalysis>(
                    source, reference, target, 16, 16, 16, prediction.Slice(3, 384), default, prepared);
            }
            if (GC.GetAllocatedBytesForCurrentThread() != allocated)
            {
                throw new InvalidDataException("Repeated seeded motion searches allocated heap storage.");
            }
            (int limitedX, int limitedY, bool exact) = H264MotionSearch.Select<H264DetailedMacroblockAnalysis>(
                source, reference, target, 16, 16, 4, prediction.Slice(3, 384), default, prepared);
            if (Math.Abs(limitedX) > 16 || Math.Abs(limitedY) > 16 || exact)
            {
                throw new InvalidDataException("Motion seeds escaped the configured search range.");
            }
            if (!target.Modes.SequenceEqual(targetModes) || !reference.Modes.SequenceEqual(referenceModes)
                || !target.MotionX(H264ReferenceList.L0).SequenceEqual(targetX)
                || !target.MotionY(H264ReferenceList.L0).SequenceEqual(targetY)
                || !reference.MotionX(H264ReferenceList.L0).SequenceEqual(referenceX)
                || !reference.MotionY(H264ReferenceList.L0).SequenceEqual(referenceY)
                || !storage.Memory.Span.SequenceEqual(original)
                || prediction[..3].ContainsAnyExcept((byte)183) || prediction[387..].ContainsAnyExcept((byte)183))
            {
                throw new InvalidDataException("Motion seeds changed picture state or prediction guards.");
            }
            return oldExact ? 0 : 1;

            void VerifyResult(H264SourceMacroblock block, H264MotionReference? interpolation)
            {
                Span<byte> actual = stackalloc byte[384];
                (int x, int y, bool exact) = H264MotionSearch.Select<H264DetailedMacroblockAnalysis>(
                    block, reference, target, 16, 16, 16, actual, default, interpolation);
                if ((x, y, exact) != (dx * 4, dy * 4, true) || !actual.SequenceEqual(block.Samples))
                {
                    throw new InvalidDataException("A valid motion seed did not recover the exact translated picture.");
                }
            }
        }
    }
}
