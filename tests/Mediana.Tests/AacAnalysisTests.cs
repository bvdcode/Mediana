// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Codecs.Aac;

namespace Mediana.Tests
{
    public class AacAnalysisTests
    {
        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(0, 0, 1)]
        [InlineData(0, 1, 0)]
        [InlineData(0, 1, 1)]
        [InlineData(1, 0, 0)]
        [InlineData(1, 0, 1)]
        [InlineData(1, 1, 0)]
        [InlineData(1, 1, 1)]
        [InlineData(2, 0, 0)]
        [InlineData(2, 0, 1)]
        [InlineData(2, 1, 0)]
        [InlineData(2, 1, 1)]
        [InlineData(3, 0, 0)]
        [InlineData(3, 0, 1)]
        [InlineData(3, 1, 0)]
        [InlineData(3, 1, 1)]
        public void WindowedAnalysisMatchesEveryDirectCoefficient(int sequence, int shape, int previousShape) =>
            AacAnalysisTestInput.VerifyTransform(sequence, shape, previousShape);

        [Fact]
        public void MixedSineAndKbdTransitionsReconstructEverySampleAndFinalTail() => AacAnalysisTestInput.VerifyTransitions();

        [Fact]
        public void CancellationPreservesSourceAndOutput()
        {
            double[] spectrum = Enumerable.Repeat(123d, 1024).ToArray();
            Assert.ThrowsAny<OperationCanceledException>(() => AacAnalysis.Transform(new double[2048], 2, 0, 0,
                spectrum, new double[2048], new double[1024], new Complex[512], new(true)));
            Assert.All(spectrum, value => Assert.Equal(123, value));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(4)]
        public void UnknownWindowSequencesFail(int sequence) => Assert.Throws<InvalidDataException>(() =>
            AacAnalysis.Transform(new double[2048], sequence, 0, 0, new double[1024], new double[2048],
                new double[1024], new Complex[512], default));
    }
}
