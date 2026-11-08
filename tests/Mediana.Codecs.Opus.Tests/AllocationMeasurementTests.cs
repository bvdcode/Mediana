// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class AllocationMeasurementTests
    {
        [Fact]
        public void AllocationChecksUseControlledGcAndDetectRealAllocations() => AllocationMeasurementTestInput.Verify();
    }
}
