// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264SliceDeblockingTestInput
    {
        public static void Verify()
        {
            foreach (bool bidirectional in new[] { false, true })
            {
                VerifyPicture(bidirectional);
            }
        }

        private static void VerifyPicture(bool bidirectional)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            H264SequenceParameterSet sequence = new()
            {
                WidthInMbs = 3,
                HeightInMapUnits = 3,
                FrameMbsOnly = true,
                Width = 48,
                Height = 48
            };
            using (H264Picture picture = new(sequence, allocator, bidirectional ? H264SliceKind.B : H264SliceKind.P))
            using (H264Picture first = new(sequence, allocator, H264SliceKind.I))
            using (H264Picture second = new(sequence, allocator, H264SliceKind.I))
            {
                H264ReferencePicture a = new(first, 0, new(0, 0));
                H264ReferencePicture b = new(second, 1, new(2, 2));
                ReadOnlySpan<int> starts = [0, 2, 4, 7];
                ReadOnlySpan<int> identities = [0, 0, 1, 1, 0, 0, 0, 1, 1];
                H264SliceHeader header = new(sequence, new(0, 0, false, false, 26, 0, 0, true, false, false, false, 1, 1, false, 0))
                { Kind = bidirectional ? H264SliceKind.B : H264SliceKind.P, ActiveReferencesL0 = 3, ActiveReferencesL1 = bidirectional ? 2 : 0 };
                H264SliceRegion[] regions = new H264SliceRegion[starts.Length];
                for (int index = 0; index < starts.Length; index++)
                {
                    int start = starts[index];
                    H264ReferencePicture active = identities[start] == 0 ? a : b;
                    H264ReferencePicture other = identities[start] == 0 ? b : a;
                    picture.FirstMacroblock = start;
                    picture.CaptureReferences([active, other, active], bidirectional ? [other, active] : []);
                    regions[index] = new(header with { FirstMacroblock = start }, index + 1 < starts.Length ? starts[index + 1] : 9);
                }
                for (int address = 0; address < identities.Length; address++)
                {
                    int x = address % 3 * 16;
                    int y = address / 3 * 16;
                    bool useL1 = bidirectional && (address & 1) != 0;
                    picture.SetMotion(H264ReferenceList.L0, x, y, 16, 16, useL1 ? -1 : (address & 1) * 2, 0, 0);
                    if (bidirectional)
                    {
                        picture.SetMotion(H264ReferenceList.L1, x, y, 16, 16, useL1 ? 1 : -1, 0, 0);
                    }
                }
                picture.Modes.Fill(H264Picture.InterMode);
                picture.Quantizers.Fill(36);
                for (int plane = 0; plane < 3; plane++)
                {
                    int size = plane == 0 ? 16 : 8;
                    int stride = picture.Stride(plane);
                    Span<byte> samples = picture.Plane(plane);
                    for (int row = 0; row < stride; row++)
                    {
                        for (int column = 0; column < stride; column++)
                        {
                            samples[row * stride + column] = (row / size + column / size) % 2 == 0 ? (byte)80 : (byte)100;
                        }
                    }
                }
                byte[] original = picture.Plane(0).ToArray();
                byte[] modes = picture.Modes.ToArray();
                byte[] indicesL0 = picture.ReferenceIndices(H264ReferenceList.L0).ToArray();
                byte[] indicesL1 = bidirectional ? picture.ReferenceIndices(H264ReferenceList.L1).ToArray() : [];
                H264Deblocking.Apply(picture, regions, default);
                for (int row = 0; row < 3; row++)
                {
                    for (int column = 0; column < 3; column++)
                    {
                        int address = row * 3 + column;
                        if (column != 0)
                        {
                            int current = (row * 16 + 8) * 48 + column * 16;
                            VerifyEdge(picture.Plane(0), original, current, 1, identities[address] != identities[address - 1]);
                        }
                        if (row != 0)
                        {
                            int current = row * 16 * 48 + column * 16 + 8;
                            VerifyEdge(picture.Plane(0), original, current, 48, identities[address] != identities[address - 3]);
                        }
                    }
                }
                if (!picture.Modes.SequenceEqual(modes) || !picture.ReferenceIndices(H264ReferenceList.L0).SequenceEqual(indicesL0)
                    || (bidirectional && !picture.ReferenceIndices(H264ReferenceList.L1).SequenceEqual(indicesL1))
                    || picture.Counts(0).ContainsAnyExcept((byte)0) || picture.MotionX(H264ReferenceList.L0).ContainsAnyExcept(0)
                    || picture.MotionY(H264ReferenceList.L0).ContainsAnyExcept(0))
                {
                    throw new InvalidDataException("Slice deblocking changed prediction metadata.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Slice deblocking retained picture storage.");
            }
        }

        private static void VerifyEdge(ReadOnlySpan<byte> actual, ReadOnlySpan<byte> original, int current, int step, bool filtered)
        {
            int delta = filtered ? (original[current] > original[current - step] ? 4 : -4) : 0;
            if (actual[current - step] != original[current - step] + delta || actual[current] != original[current] - delta)
            {
                throw new InvalidDataException("A slice reference change produced an incorrect deblocking edge.");
            }
        }
    }
}
