// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    [Collection("ImageAllocation")]
    public class PdfByteFilterTests
    {
        [Fact]
        public async Task HexWhitespaceOddNibbleAndTerminatorArePreserved() => await PdfByteFilterTestInput.VerifyAsciiHexAsync();

        [Fact]
        public async Task HexBlockBoundariesAndSplitNibblesMatchTheCompleteSource() => await PdfByteFilterTestInput.VerifyHexBlocksAsync();

        [Fact]
        public async Task Base85TuplesZeroShortcutAndPartialTailsArePreserved() => await PdfByteFilterTestInput.VerifyAscii85Async();

        [Fact]
        public async Task LiteralAndRepeatedRunBoundariesArePreserved() => await PdfByteFilterTestInput.VerifyRunLengthAsync();

        [Fact]
        public async Task MalformedByteFiltersAreRejected() => await PdfByteFilterTestInput.VerifyInvalidAsync();

        [Theory]
        [InlineData("ASCIIHexDecode")]
        [InlineData("ASCII85Decode")]
        [InlineData("RunLengthDecode")]
        public async Task ByteFiltersRespectExactLimitsAndCancellation(string filter) => await PdfByteFilterTestInput.VerifyLimitsAsync(filter);

        [Theory]
        [InlineData("ASCIIHexDecode")]
        [InlineData("ASCII85Decode")]
        [InlineData("RunLengthDecode")]
        public async Task ByteFiltersExcludeASecondCompleteDecodedAllocation(string filter) => await PdfByteFilterTestInput.VerifyAllocationAsync(filter);

        [Theory]
        [InlineData("ASCIIHexDecode", false)]
        [InlineData("ASCIIHexDecode", true)]
        [InlineData("ASCII85Decode", false)]
        [InlineData("ASCII85Decode", true)]
        [InlineData("RunLengthDecode", false)]
        [InlineData("RunLengthDecode", true)]
        public async Task InvalidOrExcessiveDataFailsBeforePayloadAllocation(string filter, bool malformed) =>
            await PdfByteFilterTestInput.VerifyRejectedAllocationAsync(filter, malformed);
    }
}
