// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264PictureLayoutTests
    {
        [Theory]
        [InlineData(true, false, 0, 18, 16, 1)]
        [InlineData(false, false, 0, 36, 16, 1)]
        [InlineData(false, true, 0, 36, 16, 2)]
        [InlineData(false, false, 1, 18, 32, 1)]
        [InlineData(false, true, 1, 18, 32, 1)]
        [InlineData(false, false, 2, 18, 32, 1)]
        [InlineData(false, true, 2, 18, 32, 1)]
        public void FrameAndFieldLayoutsUseNormativePictureAndPairAddressing(bool frameOnly, bool adaptive,
            int structureValue, int macroblocks, int maximumPictureNumber, int addressScale)
        {
            H264SequenceParameterSet sequence = new()
            {
                WidthInMbs = 6,
                HeightInMapUnits = 3,
                FrameNumberBits = 4,
                FrameMbsOnly = frameOnly,
                MbAdaptiveFrameField = adaptive
            };
            H264PictureLayout layout = new(sequence, (H264PictureStructure)structureValue);
            Assert.Equal(macroblocks, layout.MacroblockCount);
            Assert.Equal(maximumPictureNumber, layout.MaximumPictureNumber);
            Assert.Equal(0, layout.FirstMacroblockAddress(0));
            Assert.Equal(macroblocks - addressScale, layout.FirstMacroblockAddress(macroblocks / addressScale - 1));
            Assert.Throws<InvalidDataException>(() => layout.FirstMacroblockAddress(-1));
            Assert.Throws<InvalidDataException>(() => layout.FirstMacroblockAddress(macroblocks / addressScale));
        }

        [Fact]
        public void InvalidStructuresAndFieldsInFrameOnlySequencesAreRejected()
        {
            H264SequenceParameterSet sequence = new() { FrameMbsOnly = true };
            Assert.Throws<ArgumentOutOfRangeException>(() => new H264PictureLayout(sequence, (H264PictureStructure)3));
            Assert.Throws<InvalidDataException>(() => new H264PictureLayout(sequence, H264PictureStructure.TopField));
            Assert.Throws<InvalidDataException>(() => new H264PictureLayout(sequence, H264PictureStructure.BottomField));
        }
    }
}
