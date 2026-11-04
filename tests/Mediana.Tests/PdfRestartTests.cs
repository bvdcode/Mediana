// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class PdfRestartTests
    {
        [Fact]
        public Task PdfPageAndImageDecodingPreserveTheWarning() => PdfRestartTestInput.VerifyAsync();

        [Fact]
        public Task AThrowingDiagnosticHandlerDoesNotRetainMediaBuffers() => PdfRestartTestInput.VerifyCallbackFailureAsync();
    }
}
