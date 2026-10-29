// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264BidirectionalSyntaxTests
    {
        [Fact]
        public void EveryMacroblockTypeConsumesBothListsInSyntaxOrderAndFillsItsPartitions()
        {
            string[] modes = ["dd", "0", "1", "b", "00", "00", "11", "11", "01", "01", "10", "10",
                "0b", "0b", "1b", "1b", "b0", "b0", "b1", "b1", "bb", "bb"];
            for (int type = 0; type <= 21; type++)
            {
                using H264Picture target = new(Sequence(), new MediaBufferAllocator(1024), H264SliceKind.B);
                MediaBufferAllocator allocator = new(2048);
                using H264Picture past = new(Sequence(), allocator, H264SliceKind.I);
                using H264Picture future = new(Sequence(), allocator, H264SliceKind.I);
                Fill(past, 70);
                Fill(future, 170);
                H264BidirectionalReferences references = References(past, future);
                string content = string.Empty;
                foreach (char list in new[] { '0', '1' })
                {
                    foreach (char mode in modes[type])
                    {
                        if (mode == list || mode == 'b')
                        {
                            content += "11";
                        }
                    }
                }
                H264BitReader bits = new(H264TestBits.Rbsp(content));
                byte[] prediction = new byte[384];
                Assert.True(H264BidirectionalSyntax.Read(ref bits, new H264CavlcPredictionReader(), target, Header(), references, new(2, 2), 0, 0, type, prediction));
                bits.ReadTrailingBits();
                for (int row = 0; row < 16; row++)
                {
                    for (int column = 0; column < 16; column++)
                    {
                        int part = type <= 3 ? 0 : type % 2 == 0 ? row / 8 : column / 8;
                        Assert.Equal(Value(modes[type][part]), prediction[row * 16 + column]);
                    }
                }
            }
        }

        [Theory]
        [InlineData(0, 0, 1, 120, true)]
        [InlineData(1, 1, 1, 70, true)]
        [InlineData(2, 1, 1, 170, true)]
        [InlineData(3, 1, 2, 120, true)]
        [InlineData(4, 2, 1, 70, false)]
        [InlineData(5, 2, 1, 70, false)]
        [InlineData(6, 2, 1, 170, false)]
        [InlineData(7, 2, 1, 170, false)]
        [InlineData(8, 2, 2, 120, false)]
        [InlineData(9, 2, 2, 120, false)]
        [InlineData(10, 4, 1, 70, false)]
        [InlineData(11, 4, 1, 170, false)]
        [InlineData(12, 4, 2, 120, false)]
        public void EverySubMacroblockTypeConsumesItsDifferencesAndDeterminesTransformEligibility(int subtype, int parts, int lists, byte expected, bool noSmall)
        {
            MediaBufferAllocator allocator = new(4096);
            using H264Picture target = new(Sequence(), allocator, H264SliceKind.B);
            using H264Picture past = new(Sequence(), allocator, H264SliceKind.I);
            using H264Picture future = new(Sequence(), allocator, H264SliceKind.I);
            Fill(past, 70);
            Fill(future, 170);
            string content = string.Concat(Enumerable.Repeat(H264TestBits.Ue(subtype), 4))
                + (subtype == 0 ? string.Empty : new string('1', parts * lists * 4 * 2));
            H264BitReader bits = new(H264TestBits.Rbsp(content));
            byte[] prediction = new byte[384];
            Assert.Equal(noSmall, H264BidirectionalSyntax.Read(ref bits, new H264CavlcPredictionReader(), target, Header(), References(past, future), new(2, 2), 0, 0, 22, prediction));
            bits.ReadTrailingBits();
            Assert.All(prediction, value => Assert.Equal(expected, value));
        }

        [Fact]
        public void ReferenceIndicesForBothListsPrecedeBothListsMotionDifferences()
        {
            MediaBufferAllocator allocator = new(4096);
            using H264Picture target = new(Sequence(), allocator, H264SliceKind.B);
            using H264Picture past = new(Sequence(), allocator, H264SliceKind.I);
            using H264Picture future = new(Sequence(), allocator, H264SliceKind.I);
            H264BidirectionalReferences references = new([new(past, 0, new(0, 0)), new(future, 1, new(4, 4))],
                [new(future, 1, new(4, 4)), new(past, 0, new(0, 0))]);
            H264BitReader bits = new(H264TestBits.Rbsp("1001 010 1 00100 1 00110 1 0001000 1"));
            H264BidirectionalSyntax.Read(ref bits, new H264CavlcPredictionReader(), target, Header() with { ActiveReferencesL0 = 2, ActiveReferencesL1 = 2 }, references,
                new(2, 2), 0, 0, 20, new byte[384]);
            bits.ReadTrailingBits();
            Assert.Equal(new H264MotionSample(true, 0, 1, 0), target.MotionAt(H264ReferenceList.L0, 0, 0));
            Assert.Equal(new H264MotionSample(true, 1, 2, 0), target.MotionAt(H264ReferenceList.L0, 0, 8));
            Assert.Equal(new H264MotionSample(true, 1, 3, 0), target.MotionAt(H264ReferenceList.L1, 0, 0));
            Assert.Equal(new H264MotionSample(true, 0, 4, 0), target.MotionAt(H264ReferenceList.L1, 0, 8));
        }

        private static byte Value(char mode) => mode switch
        {
            '0' => 70,
            '1' => 170,
            'b' or 'd' => 120,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        private static void Fill(H264Picture picture, byte value)
        {
            for (int plane = 0; plane < 3; plane++)
            {
                picture.Plane(plane).Fill(value);
            }
        }

        private static H264BidirectionalReferences References(H264Picture past, H264Picture future) =>
            new([new(past, 0, new(0, 0))], [new(future, 1, new(4, 4))]);

        private static H264SequenceParameterSet Sequence() => new()
        { WidthInMbs = 1, HeightInMapUnits = 1, FrameMbsOnly = true, Direct8x8Inference = true };

        private static H264SliceHeader Header() => new(Sequence(),
            new H264PictureParameterSet(0, 0, false, false, 26, 0, 0, true, false, false, false, 1, 1, false, 0))
        { Kind = H264SliceKind.B, ActiveReferencesL0 = 1, ActiveReferencesL1 = 1, DirectSpatialMotionPrediction = true };
    }
}
