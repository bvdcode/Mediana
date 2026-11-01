// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class MatroskaColourTests
    {
        [Fact]
        public Task NativeColourValuesAndSingletonRulesArePreserved() => MatroskaColourTestInput.VerifyNativeFieldsAsync();
        [Fact]
        public Task SpecifiedColourAndChromaAxesResolveOverCodecLabels() => MatroskaColourTestInput.VerifyColorResolutionAsync();
        [Fact]
        public void PresentationMapsExactAspectRatioAndDeclinesUnrepresentedTransforms() => MatroskaColourTestInput.VerifyPresentationMapping();
    }
}
