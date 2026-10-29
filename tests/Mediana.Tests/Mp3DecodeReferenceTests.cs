// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class Mp3DecodeReferenceTests
    {
        [Fact]
        public async Task EveryPcmSampleMatchesIndependentCompleteFileReferences()
        {
            await Mp3DecodeTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp3"));
        }
    }
}
