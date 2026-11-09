// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1GrainDisplayTests
    {
        [Fact]
        public void ReconstructsIndependentGrainPicturesAcrossSeedsParametersAndClippedGeometry() =>
            Av1GrainDisplayTestInput.VerifyPictures(Path.Combine(AppContext.BaseDirectory, "Data"));

        [Fact]
        public void HeldDisplayStorageSurvivesReferenceUpdatesResetAndCancellation() =>
            Av1GrainDisplayTestInput.VerifyOwnership(Path.Combine(AppContext.BaseDirectory, "Data"));
    }
}
