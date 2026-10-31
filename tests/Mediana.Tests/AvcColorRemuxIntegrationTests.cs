// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class AvcColorRemuxIntegrationTests
    {
        [Fact]
        public Task RealContainerColorsSurviveRemuxAndOutOfOrderHlsWithoutChangingSamplesOrTimestamps() =>
            AvcColorRemuxTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Preview"));
    }
}
