// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264BidirectionalDeblockingTestInput
    {
        public static void Verify()
        {
            MediaBufferAllocator allocator = new(8192);
            H264SequenceParameterSet sequence = new()
            {
                WidthInMbs = 2,
                HeightInMapUnits = 1,
                FrameMbsOnly = true,
                Width = 32,
                Height = 16
            };
            using (H264Picture picture = new(sequence, allocator, H264SliceKind.B))
            using (H264Picture first = new(sequence, allocator, H264SliceKind.I))
            using (H264Picture second = new(sequence, allocator, H264SliceKind.I))
            {
                H264ReferencePicture a = new(first, 0, new H264PictureOrder(0, 0));
                H264ReferencePicture b = new(second, 1, new H264PictureOrder(4, 4));
                IReadOnlyList<H264ReferencePicture?> l0 = [a, b, a];
                IReadOnlyList<H264ReferencePicture?> l1 = [b, a];
                ReadOnlySpan<(int P0, int P1, int Q0, int Q1, int Px0, int Px1, int Qx0, int Qx1, int Strength)> cases =
                [
                    (0, -1, -1, 1, 0, 0, 0, 3, 0),
                    (0, -1, -1, 1, 0, 0, 0, 4, 1),
                    (0, -1, -1, 0, 0, 0, 0, 0, 1),
                    (0, -1, 0, 1, 0, 0, 0, 0, 1),
                    (0, 0, 0, 0, 0, 16, 3, 13, 0),
                    (0, 0, 0, 0, 0, 16, 4, 16, 1),
                    (0, 0, 0, 0, 0, 16, 0, 12, 1),
                    (0, 0, 1, 1, 0, 16, 16, 0, 0),
                    (0, 0, 1, 1, 0, 16, 20, 0, 1),
                    (0, 1, 2, 1, 0, 16, 0, 16, 0),
                    (0, 1, 2, 1, 0, 16, 16, 0, 0),
                    (0, 1, 2, 1, 0, 16, 3, 13, 0),
                    (0, 1, 2, 1, 0, 16, 4, 12, 1),
                    (0, 1, 0, -1, 0, 0, 0, 0, 1)
                ];
                foreach ((int P0, int P1, int Q0, int Q1, int Px0, int Px1, int Qx0, int Qx1, int Strength) item in cases)
                {
                    picture.SetMotion(H264ReferenceList.L0, 0, 0, 16, 16, item.P0, item.Px0, 0);
                    picture.SetMotion(H264ReferenceList.L1, 0, 0, 16, 16, item.P1, item.Px1, 0);
                    picture.SetMotion(H264ReferenceList.L0, 16, 0, 16, 16, item.Q0, item.Qx0, 0);
                    picture.SetMotion(H264ReferenceList.L1, 16, 0, 16, 16, item.Q1, item.Qx1, 0);
                    picture.Modes.Fill(H264Picture.InterMode);
                    if (H264Deblocking.BoundaryStrength(picture, 16, 0, true, true, l0, l1) != item.Strength)
                    {
                        throw new InvalidDataException("B deblocking differs from the reference identity and motion boundary vectors.");
                    }
                }
                H264SliceHeader header = new(sequence, new H264PictureParameterSet(0, 0, false, false, 26, 0, 0,
                    true, false, false, false, 1, 1, false, 0))
                { Kind = H264SliceKind.B };
                picture.Quantizers.Fill(36);
                picture.SetMotion(H264ReferenceList.L0, 0, 0, 32, 16, 0, 0, 0);
                picture.SetMotion(H264ReferenceList.L1, 0, 0, 32, 16, -1, 0, 0);
                picture.SetMotion(H264ReferenceList.L0, 16, 0, 16, 16, -1, 0, 0);
                picture.SetMotion(H264ReferenceList.L1, 16, 0, 16, 16, 1, 0, 4);
                picture.Modes.Fill(H264Picture.InterMode);
                for (int plane = 0; plane < 3; plane++)
                {
                    int width = picture.Stride(plane);
                    for (int row = 0; row < picture.Plane(plane).Length / width; row++)
                    {
                        picture.Plane(plane).Slice(row * width, width / 2).Fill(80);
                        picture.Plane(plane).Slice(row * width + width / 2, width / 2).Fill(100);
                    }
                }
                H264Deblocking.Apply(picture, header, default, l0, l1);
                if (picture.Plane(0)[15] != 84 || picture.Plane(0)[16] != 96)
                {
                    throw new InvalidDataException("The B inter edge did not apply its expected weak filter.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("B deblocking verification retained picture storage.");
            }
        }
    }
}
