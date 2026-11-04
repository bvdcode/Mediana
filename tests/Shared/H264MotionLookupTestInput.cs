// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264MotionLookupTestInput
    {
        public static int Verify()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            int cases = 0;
            using (H264Picture picture = Create(allocator, H264SliceKind.B))
            {
                cases += VerifyValues(picture, H264ReferenceList.L0);
                cases += VerifyValues(picture, H264ReferenceList.L1);
                cases += VerifyGeometry(picture);
                VerifyErrors(picture);
                Repeat(picture);
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                Repeat(picture);
                Require(GC.GetAllocatedBytesForCurrentThread() == allocated);
            }
            using (H264Picture picture = Create(allocator, H264SliceKind.P))
            {
                ExpectUnavailableList(picture);
                picture.Dispose();
                ExpectDisposed(picture);
            }
            Require(allocator.AllocatedBytes == 0);
            return cases;
        }

        private static int VerifyValues(H264Picture picture, H264ReferenceList list)
        {
            Span<byte> modes = picture.Modes;
            Span<byte> references = picture.ReferenceIndices(list);
            Span<int> vectorsX = picture.MotionX(list);
            Span<int> vectorsY = picture.MotionY(list);
            int offset = 4 * 12 + 4;
            int cases = 0;
            for (int mode = 0; mode <= byte.MaxValue; mode++)
            {
                for (int reference = 0; reference <= byte.MaxValue; reference++)
                {
                    for (int pattern = 0; pattern < 4; pattern++)
                    {
                        (int x, int y) = pattern switch
                        {
                            0 => (int.MinValue, int.MaxValue),
                            1 => (int.MaxValue, int.MinValue),
                            2 => (-65539, 65539),
                            3 => (0, 0),
                            _ => throw new ArgumentOutOfRangeException(nameof(pattern))
                        };
                        modes[offset] = (byte)mode;
                        references[offset] = (byte)reference;
                        vectorsX[offset] = x;
                        vectorsY[offset] = y;
                        H264MotionSample expected = mode == 255 ? new(false, -1, 0, 0)
                            : (mode == 9 || mode == 137) && reference != 255 ? new(true, reference, x, y)
                            : new(true, -1, 0, 0);
                        Require(picture.MotionAt(list, 16, 16) == expected);
                        Require(modes[offset] == mode && references[offset] == reference
                            && vectorsX[offset] == x && vectorsY[offset] == y);
                        cases++;
                    }
                }
            }
            return cases;
        }

        private static int VerifyGeometry(H264Picture picture)
        {
            picture.Modes.Fill(137);
            picture.ReferenceIndices(H264ReferenceList.L0).Fill(7);
            picture.MotionX(H264ReferenceList.L0).Fill(int.MinValue);
            picture.MotionY(H264ReferenceList.L0).Fill(int.MaxValue);
            int[] coordinates = [int.MinValue, -1, 0, 1, 3, 4, 15, 16, 31, 32, 47, 48, int.MaxValue];
            int cases = 0;
            foreach (int first in new[] { int.MinValue, -1, 0, 1, 4, 8, 9, int.MaxValue })
            {
                picture.FirstMacroblock = first;
                foreach (int y in coordinates)
                {
                    foreach (int x in coordinates)
                    {
                        bool available = x >= 0 && x < 48 && y >= 0 && y < 48 && (y / 16) * 3 + x / 16 >= first;
                        H264MotionSample expected = available ? new(true, 7, int.MinValue, int.MaxValue) : new(false, -1, 0, 0);
                        Require(picture.MotionAt(H264ReferenceList.L0, x, y) == expected);
                        cases++;
                    }
                }
            }
            picture.FirstMacroblock = 0;
            return cases;
        }

        private static void VerifyErrors(H264Picture picture)
        {
            try
            {
                _ = picture.MotionAt((H264ReferenceList)123, -1, -1);
            }
            catch (ArgumentOutOfRangeException exception) when (exception.ParamName == "list")
            {
                Console.WriteLine("PASS invalid motion-list error.");
                return;
            }
            throw new InvalidDataException("Invalid motion list was accepted.");
        }

        private static void ExpectUnavailableList(H264Picture picture)
        {
            try
            {
                _ = picture.MotionAt(H264ReferenceList.L1, -1, -1);
            }
            catch (InvalidOperationException exception) when (exception.Message == "This H.264 picture has no L1 motion storage.")
            {
                Console.WriteLine("PASS unavailable motion-list error.");
                return;
            }
            throw new InvalidDataException("Unavailable motion list was accepted.");
        }

        private static void ExpectDisposed(H264Picture picture)
        {
            try
            {
                _ = picture.MotionAt(H264ReferenceList.L0, -1, -1);
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine("PASS disposed motion owner error.");
                return;
            }
            throw new InvalidDataException("Disposed motion owner was accepted.");
        }

        private static void Repeat(H264Picture picture)
        {
            for (int index = 0; index < 1024; index++)
            {
                Require(picture.MotionAt(H264ReferenceList.L0, 16, 16).ReferenceIndex == 7);
            }
        }

        private static H264Picture Create(MediaBufferAllocator allocator, H264SliceKind kind) => new(new()
        {
            WidthInMbs = 3,
            HeightInMapUnits = 3,
            FrameMbsOnly = true
        }, allocator, kind);

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("Motion lookup differs from the independent result.");
            }
        }
    }
}
