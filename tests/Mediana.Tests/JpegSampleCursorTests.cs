// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class JpegSampleCursorTests
    {
        [Fact]
        public void IncrementalSamplesMatchTheCoordinateDefinitionForEverySamplingOrientationAndPhase() => JpegSampleCursorTestInput.Verify();
    }
}
