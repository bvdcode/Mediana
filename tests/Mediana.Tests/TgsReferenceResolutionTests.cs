// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class TgsReferenceResolutionTests
    {
        [Fact]
        public void ParameterGroupsDoNotCreateVisualEffects() => TgsEffectTestInput.VerifyParameterGroups();

        [Fact]
        public void UnreferencedDuplicateIndicesPreserveLayerOrder() => TgsLayerReferenceTestInput.VerifyUnreferencedDuplicates();

        [Fact]
        public void AmbiguousParentAndMatteReferencesAreRejected() => TgsLayerReferenceTestInput.VerifyAmbiguousReferences();
    }
}
