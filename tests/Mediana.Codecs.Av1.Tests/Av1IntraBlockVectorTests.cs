// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1IntraBlockVectorTests
    {
        [Theory]
        [InlineData(0, 80, 16, 16, 0, -2560, true)]
        [InlineData(0, 64, 16, 16, 0, -2048, false)]
        [InlineData(16, 0, 16, 16, -512, 0, true)]
        [InlineData(16, 0, 32, 16, -512, 0, false)]
        [InlineData(0, 80, 16, 16, 0, -2559, false)]
        [InlineData(0, 80, 16, 16, 0, -16384, false)]
        [InlineData(0, 80, 16, 16, 0, 16384, false)]
        [InlineData(0, 80, 16, 16, -8, -2560, false)]
        [InlineData(0, 80, 16, 16, 0, 0, false)]
        public void EnforcesWholePixelsTileBoundsAndTheDecodedWavefront(int row, int column, int width, int height,
            int motionY, int motionX, bool expected)
        {
            Av1SequenceHeader sequence = default(Av1SequenceHeader) with { Width = 512, Height = 192 };
            Av1TileBounds bounds = new(0, sequence.MiRows, 0, sequence.MiColumns);
            Assert.Equal(expected, Av1IntraBlockVectorValidator.IsValid(sequence, bounds, row, column, width, height, motionY, motionX, true));
        }

        [Fact]
        public void DoesNotReferenceAnEarlierTileEvenWhenItsPixelsAreDecoded()
        {
            Av1SequenceHeader sequence = default(Av1SequenceHeader) with { Width = 1024, Height = 384 };
            Av1TileBounds bounds = new(0, 48, 128, 256);
            Assert.True(Av1IntraBlockVectorValidator.IsValid(sequence, bounds, 0, 208, 16, 16, 0, -2560, true));
            Assert.False(Av1IntraBlockVectorValidator.IsValid(sequence, bounds, 0, 208, 16, 16, 0, -2624, true));
        }

        [Fact]
        public void ChromaSub8BlocksRequireThePrecedingSharedSamples()
        {
            Av1SequenceHeader sequence = default(Av1SequenceHeader) with { Width = 512, Height = 192 };
            Av1TileBounds bounds = new(0, 48, 0, 128);
            Assert.True(Av1IntraBlockVectorValidator.IsValid(sequence, bounds, 16, 1, 1, 2, -512, -32, false));
            Assert.False(Av1IntraBlockVectorValidator.IsValid(sequence, bounds, 16, 1, 1, 2, -512, -32, true));
        }

        [Fact]
        public void LargeSuperblocksUseTheSpecifiedWavefrontGradient()
        {
            Av1SequenceHeader sequence = default(Av1SequenceHeader) with { Width = 768, Height = 384, Superblock128 = true };
            Av1TileBounds bounds = new(0, sequence.MiRows, 0, sequence.MiColumns);
            Assert.True(Av1IntraBlockVectorValidator.IsValid(sequence, bounds, 32, 0, 32, 32, -1024, 0, true));
            Assert.False(Av1IntraBlockVectorValidator.IsValid(sequence, bounds, 32, 0, 32, 32, -1024, 512, true));
        }

        [Fact]
        public void IntraCopyFlagPreservesTheExistingSixteenByteMotionGrid()
        {
            Assert.Equal(16, Unsafe.SizeOf<Av1MotionBlock>());
            Av1MotionBlock block = new() { FilterX = 2, InterIntra = true, Intrabc = true };
            Assert.True(block.IsInter);
            Assert.Equal(0, block.Reference);
            Assert.Equal(2, block.FilterX);
            Assert.True(block.InterIntra);
            block.FilterX = 3;
            block.InterIntra = false;
            Assert.True(block.Intrabc);
            Assert.Equal(3, block.FilterX);
            block.Intrabc = false;
            Assert.False(block.IsInter);
            block.Reference = 1;
            Assert.True(block.IsInter);
        }
    }
}
