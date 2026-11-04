// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class JpegRestartTests
    {
        [Fact]
        public Task FinalExpectedRestartPreservesFullAndReducedPixelsWithDiagnostics() => JpegRestartTestInput.VerifyRecoveryAsync();

        [Fact]
        public Task WrongPrematureRepeatedOrTruncatedRestartsRemainErrors() => JpegRestartTestInput.VerifyRejectedAsync();

    }
}
