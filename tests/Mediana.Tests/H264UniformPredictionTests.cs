// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class H264UniformPredictionTests
    {
        [Fact]
        public void UniformPredictionAndEveryCellMutationPreserveBoundaryStrengths() => H264UniformPredictionTestInput.Verify();

        [Fact]
        public void MutableContextsRevalidateInternalReferencesWithoutAllocation() => H264UniformContextTestInput.Verify();
    }
}
