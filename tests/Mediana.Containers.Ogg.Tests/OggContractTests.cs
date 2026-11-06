// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Ogg.Tests
{
    public class OggContractTests
    {
        [Fact]
        public Task VerifyPublicReaderContract() => Mediana.Tests.OggContainerTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));
    }
}
