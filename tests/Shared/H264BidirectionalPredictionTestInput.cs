using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264BidirectionalPredictionTestInput
    {
        public static void Verify()
        {
            MediaBufferAllocator allocator = new(4096);
            H264SliceHeader header = Header();
            using (H264Picture first = new(header.Sequence, allocator, H264SliceKind.P))
            using (H264Picture second = new(header.Sequence, allocator, H264SliceKind.P))
            using (H264Picture target = new(header.Sequence, allocator, H264SliceKind.B))
            {
                H264BidirectionalReferences references = new([new(first, 0, new(0, 0))], [new(second, 1, new(6, 6))]);
                for (int plane = 0; plane < 3; plane++)
                {
                    first.Plane(plane).Fill(70);
                    second.Plane(plane).Fill(170);
                }
                byte[] prediction = new byte[384];
                for (int mode = 0; mode <= 2; mode++)
                {
                    H264SliceHeader selected = header with { Picture = header.Picture with { WeightedBiPrediction = mode } };
                    for (int selection = 0; selection < 3; selection++)
                    {
                        (H264PartitionMotion L0, H264PartitionMotion L1, byte[] Expected) item = (mode, selection) switch
                        {
                            (0 or 2, 0) => (new(0, 0, 0), H264PartitionMotion.None, [70, 70, 70]),
                            (0 or 2, 1) => (H264PartitionMotion.None, new(0, 0, 0), [170, 170, 170]),
                            (0, 2) => (new(0, 0, 0), new(0, 0, 0), [120, 120, 120]),
                            (2, 2) => (new(0, 0, 0), new(0, 0, 0), [103, 103, 103]),
                            (1, 0) => (new(0, 0, 0), H264PartitionMotion.None, [0, 36, 1]),
                            (1, 1) => (H264PartitionMotion.None, new(0, 0, 0), [255, 209, 83]),
                            (1, 2) => (new(0, 0, 0), new(0, 0, 0), [110, 123, 43]),
                            _ => throw new InvalidOperationException("Invalid bidirectional prediction test selection.")
                        };
                        prediction.AsSpan().Fill(204);
                        H264BidirectionalPrediction.PredictPartition(target, selected, references, new(2, 2), 4, 4, 8, 8, item.L0, item.L1, prediction);
                        VerifyBlock(prediction, item.Expected, gradient: false);
                        if (target.MotionAt(H264ReferenceList.L0, 4, 4).ReferenceIndex != item.L0.ReferenceIndex
                            || target.MotionAt(H264ReferenceList.L1, 4, 4).ReferenceIndex != item.L1.ReferenceIndex)
                        {
                            throw new InvalidDataException("B partition motion storage differs from its prediction lists.");
                        }
                    }
                }
                FillGradient(first, second);
                prediction.AsSpan().Fill(204);
                H264BidirectionalPrediction.PredictPartition(target, header, references, new(2, 2), 4, 4, 8, 8, new(0, 2, 2), new(0, -2, 0), prediction);
                VerifyBlock(prediction, [61, 71, 91], gradient: true);
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("B partition prediction retained picture storage.");
            }
        }

        public static H264SliceHeader Header()
        {
            H264SequenceParameterSet sequence = new()
            {
                WidthInMbs = 1,
                HeightInMapUnits = 1,
                FrameMbsOnly = true,
                Width = 16,
                Height = 16
            };
            H264PictureParameterSet picture = new(0, 0, false, false, 26, 0, 0, true, false, false, false, 1, 1, false, 0);
            return new H264SliceHeader(sequence, picture)
            {
                Kind = H264SliceKind.B,
                ActiveReferencesL0 = 1,
                ActiveReferencesL1 = 1,
                Weights = new H264WeightTable(1, 2, [new(-1, -3, 2, 1, 0, 1)]),
                WeightsL1 = new H264WeightTable(1, 2, [new(3, 2, 5, -4, 2, -2)])
            };
        }

        private static void FillGradient(H264Picture first, H264Picture second)
        {
            for (int plane = 0; plane < 3; plane++)
            {
                int size = plane == 0 ? 16 : 8;
                (int start0, int start1, int dx0, int dy0, int dx1, int dy1) = plane switch
                {
                    0 => (20, 100, 2, 4, 4, 2),
                    1 => (40, 100, 2, 4, 4, 2),
                    2 => (60, 120, 4, 2, 2, 4),
                    _ => throw new InvalidOperationException("Invalid prediction component.")
                };
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        first.Plane(plane)[y * size + x] = (byte)(start0 + x * dx0 + y * dy0);
                        second.Plane(plane)[y * size + x] = (byte)(start1 + x * dx1 + y * dy1);
                    }
                }
            }
        }

        private static void VerifyBlock(ReadOnlySpan<byte> prediction, ReadOnlySpan<byte> expected, bool gradient)
        {
            int offset = 0;
            for (int plane = 0; plane < 3; plane++)
            {
                int scale = plane == 0 ? 1 : 2;
                int size = 16 / scale;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int value = 204;
                        if (x >= 4 / scale && x < 12 / scale && y >= 4 / scale && y < 12 / scale)
                        {
                            value = expected[plane] + (gradient ? 3 * x + 3 * y : 0);
                        }
                        if (prediction[offset + y * size + x] != value)
                        {
                            throw new InvalidDataException($"B partition prediction differs at plane {plane}, x {x}, y {y}.");
                        }
                    }
                }
                offset += size * size;
            }
        }
    }
}
