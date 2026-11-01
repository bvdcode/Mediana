// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class H264FractionalSearchTests
    {
        [Fact]
        public void PreparedNeighborhoodsMatchIndependentConvolutionAtAllOffsetsAndBorders() => H264LumaNeighborhoodTestInput.Verify();

        [Fact]
        public void RepeatedNeighborhoodPreparationAndReadAllocateNoHeapStorage() => H264LumaNeighborhoodTestInput.VerifyAllocation();

        [Fact]
        public async Task SearchMatchesIndependentVectorsPredictionsAndSourceOwnership() => await H264MotionSearchTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"));
    }
}
