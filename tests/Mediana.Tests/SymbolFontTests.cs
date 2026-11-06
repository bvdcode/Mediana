// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class SymbolFontTests
    {
        [Fact]
        public void SymbolsRespectPoliciesSharedStorageAndLimits() => SymbolFontTestInput.VerifyResolution();

        [Fact]
        public Task SymbolsRenderThroughSharedWordSvgAndPdfResources() => SymbolFontTestInput.VerifyDocumentsAsync();
    }
}
