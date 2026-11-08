// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1CompoundMotionTests
    {
        [Fact]
        public void DualMotionStoragePreservesSixteenByteGridAndIndependentFlags()
        {
            Av1MotionBlock block = new()
            {
                Reference = 3, SecondReference = 7, Mode = 24, Width = 16, Height = 8,
                Row = -16383, Column = 16383, SecondRow = 16382, SecondColumn = -16382,
                SkipMode = true, CompoundGroup = 1, CompoundIndex = 1, MotionMode = 2,
                FilterX = 2, FilterY = 1, Transform = 4, TransformSize = 9
            };
            Assert.Equal(16, Unsafe.SizeOf<Av1MotionBlock>());
            Assert.Equal(3, block.Reference);
            Assert.Equal(7, block.SecondReference);
            Assert.Equal(24, block.Mode);
            Assert.Equal(-16383, block.Row);
            Assert.Equal(16383, block.Column);
            block.Reference = 1;
            block.SecondReference = 4;
            block.Mode = 17;
            block.SkipMode = false;
            block.CompoundIndex = 0;
            Assert.Equal(1, block.Reference);
            Assert.Equal(4, block.SecondReference);
            Assert.Equal(17, block.Mode);
            Assert.False(block.SkipMode);
            Assert.Equal(1, block.CompoundGroup);
            Assert.Equal(0, block.CompoundIndex);
            Assert.Equal(16382, block.SecondRow);
            Assert.Equal(-16382, block.SecondColumn);
            Assert.Equal(9, block.TransformSize);
            Assert.Equal(2, block.MotionMode);
            block.MotionMode = 1;
            Assert.Equal(17, block.Mode);
            Assert.Equal(0, block.CompoundIndex);
        }

        [Fact]
        public void PairedCandidatesKeepBothVectorsAndSingleSearchIncludesSecondList()
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "interOrdered.obu"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (Av1PictureDecoder decoder = new(allocator))
            using (Av1Picture picture = decoder.Decode(Av1SequencePictureTests.AccessUnits(encoded)[0].Span))
            using (Av1ReferencePictures references = new())
            {
                Av1SequenceHeader sequence = picture.Sequence with { Tools = picture.Sequence.Tools with { OrderHintBits = 0 } };
                Av1FrameHeader header = picture.Header with { Inter = new(default, 7, false, true, 0, false, false) };
                Av1MotionBlock[] blocks = new Av1MotionBlock[sequence.MiRows * sequence.MiColumns];
                blocks.AsSpan().Fill(new() { Width = 2, Height = 2 });
                blocks[3 * sequence.MiColumns + 4] = new()
                {
                    Reference = 1, SecondReference = 2, Width = 2, Height = 2,
                    Row = 8, Column = -16, SecondRow = -24, SecondColumn = 32, Mode = 24
                };
                blocks[4 * sequence.MiColumns + 3] = new()
                {
                    Reference = 1, SecondReference = 2, Width = 2, Height = 2,
                    Row = 8, Column = -16, SecondRow = -40, SecondColumn = 48, Mode = 17
                };
                Span<Av1MotionCandidate> storage = stackalloc Av1MotionCandidate[8];
                Av1MotionCandidates pair = new(storage, blocks, sequence, header, references, 4, 4, 2, 2, 1, secondReference: 2);
                pair.Build();
                Assert.Equal(2, pair.Count);
                Assert.Equal(new Av1MotionCandidate(8, -16, 644, -24, 32), pair[0]);
                Assert.Equal(new Av1MotionCandidate(8, -16, 644, -40, 48), pair[1]);
                Assert.Equal(4, pair.NewContext);
                Av1MotionCandidates single = new(storage, blocks, sequence, header, references, 4, 4, 2, 2, 2);
                single.Build();
                Assert.Equal(2, single.Count);
                Assert.Equal(new Av1MotionCandidate(-24, 32, 644), single[0]);
                Assert.Equal(new Av1MotionCandidate(-40, 48, 644), single[1]);
                Av1MotionCandidates empty = new(storage, blocks, sequence, header, references, 0, 0, 2, 2, 1, secondReference: 2);
                empty.Build();
                Assert.Equal(2, empty.Count);
                Assert.Equal(new Av1MotionCandidate(0, 0, 2), empty[0]);
                Assert.Equal(new Av1MotionCandidate(0, 0, 2), empty[1]);
                blocks.AsSpan().Fill(new() { Width = 2, Height = 2 });
                blocks[3 * sequence.MiColumns + 4] = new()
                {
                    Reference = 1, SecondReference = 2, Width = 2, Height = 2,
                    Row = 16, Column = 28, SecondRow = 8, SecondColumn = 14, Mode = 17
                };
                blocks[sequence.MiColumns + 5] = new()
                {
                    Reference = 6, Width = 2, Height = 2, Row = 40, Column = 80, Mode = 13
                };
                Av1MotionCandidates third = new(storage, blocks, sequence, header, references, 4, 4, 2, 2, 6);
                third.Build();
                Assert.Equal(3, third.Count);
                Assert.Equal(new Av1MotionCandidate(40, 80, 4), third[0]);
                Assert.Equal(new Av1MotionCandidate(16, 28, 2), third[1]);
                Assert.Equal(new Av1MotionCandidate(8, 14, 2), third[2]);
                Assert.Equal(2, third.DrlContext(1));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
