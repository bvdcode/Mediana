// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Metadata;

namespace Mediana.Tests
{
    public class Bcp47LanguageTagTests
    {
        [Fact]
        public void RfcExamplesAndMalformedSubtagsMatchGrammar() => LanguageTagTestInput.Verify();

        [Fact]
        public void LongPrivateTagsAreParsedWithoutPerSubtagAllocations()
        {
            string value = "x" + string.Concat(Enumerable.Repeat("-abcdefgh", 20000));
            Assert.True(Bcp47LanguageTag.IsWellFormed("x-one"));
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool wellFormed = Bcp47LanguageTag.IsWellFormed(value);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(wellFormed);
            Assert.InRange(allocated, 0, 1024);
        }
    }
}
