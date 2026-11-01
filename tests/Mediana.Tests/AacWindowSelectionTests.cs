// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;

namespace Mediana.Tests
{
    public class AacWindowSelectionTests
    {
        [Fact]
        public void EverySixBlockAttackPatternPreservesTransitionsAndEveryUnquantizedSample()
        {
            for (int pattern = 0; pattern < 64; pattern++)
            {
                AacWindowSelectionTestInput.VerifyDecisions(pattern);
            }
        }

        [Fact]
        public void PeakDetectionDistinguishesSilenceAttacksAndStationarySignal() => AacWindowSelectionTestInput.VerifyDetector();

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void GroupingPreservesSilenceStationaryEnergyAndQuietChannelAttacks(int channels)
        {
            for (int shape = 0; shape < 2; shape++)
            {
                for (int previousShape = 0; previousShape < 2; previousShape++)
                {
                    AacWindowSelectionTestInput.VerifyGrouping(channels, shape, previousShape);
                }
            }
        }

        [Fact]
        public void InconsistentLookaheadIsRejected()
        {
            AacWindowSelector selector = new();
            _ = selector.Select(false, true);
            Assert.Throws<InvalidOperationException>(() => selector.Select(false, false));
        }

        [Fact]
        public void CancellationPrecedesDetectorStateChanges()
        {
            AacTransientDetector detector = new();
            double[] block = new double[1024];
            block[0] = 30000;
            Assert.ThrowsAny<OperationCanceledException>(() => detector.Analyze(block, new(true)));
            Assert.True(detector.Analyze(block, default));
        }
    }
}
