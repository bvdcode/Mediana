// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class ContainerColorIntegrationTests
    {
        [Fact]
        public Task NativeRangeSamplesAndContainerColorPrecedenceSurviveDecodeProcessingAndEncoding() =>
            ContainerColorTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Preview"));
    }
}
