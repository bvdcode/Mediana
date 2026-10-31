// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264IntraModeTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        public void EveryDirectionSelectsExactIndependentSamples(int mode) => H264IntraModeTestInput.VerifyMode(mode);

        [Fact]
        public void AllNeighbourAvailabilityCombinationsMatchTheSpecification() => H264IntraModeTestInput.VerifyAvailability();

        [Fact]
        public void ExactFlatPredictionOmitsResidualGroupsAndPrefersPredictedMode() => H264IntraModeTestInput.VerifyEmptyResidual();

        [Theory]
        [InlineData(-1)]
        [InlineData(9)]
        public void UnknownDirectionalModeIsRejected(int mode) =>
            Assert.Throws<InvalidDataException>(() => H264IntraPrediction.IsLumaModeAvailable(mode, true, true, true));
    }
}
