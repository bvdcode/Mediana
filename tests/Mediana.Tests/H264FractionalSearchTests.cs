// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class H264FractionalSearchTests
    {
        [Fact]
        public void VectorDifferencesMatchEveryBytePairAndMixedLaneSums() => H264SampleDifferenceTestInput.Verify();

        [Fact]
        public void PairedChromaCostsMatchBilinearPredictionAtAllPhasesBordersAndCutoffs() =>
            Assert.Equal(660480, H264ChromaDifferenceTestInput.Verify());

        [Fact]
        public void CandidateEvaluationPreservesEveryRowRejectionBoundaryAndExactTies() => H264LumaCandidateTestInput.Verify();

        [Fact]
        public void PreparedNeighborhoodsMatchIndependentConvolutionAtAllOffsetsAndBorders() => H264LumaNeighborhoodTestInput.Verify();

        [Fact]
        public void ShortIntermediateFilteringMatchesWideConvolutionAcrossItsCompleteDomain() =>
            Assert.Equal(334904, H264ShortInterpolationTestInput.Verify());

        [Fact]
        public void RepeatedNeighborhoodPreparationAndReadAllocateNoHeapStorage() => H264LumaNeighborhoodTestInput.VerifyAllocation();

        [Fact]
        public async Task SearchMatchesIndependentVectorsPredictionsAndSourceOwnership() => await H264MotionSearchTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"));
    }
}
