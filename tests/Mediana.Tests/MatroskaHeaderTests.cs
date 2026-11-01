// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class MatroskaHeaderTests
    {
        [Fact]
        public Task DocumentDefaultsAndFutureWriterVersionsFollowTheSchema() => MatroskaHeaderTestInput.VerifyDefaultsAndVersionsAsync();

        [Fact]
        public Task MissingDuplicateAndInvalidFieldsAreRejected() => MatroskaHeaderTestInput.VerifyInvalidFieldsAsync();

        [Fact]
        public Task CrcUsesTheIeeePolynomialAndLittleEndianStorage() => MatroskaHeaderTestInput.VerifyCrcAsync();

        [Fact]
        public Task HeadersRespectConfiguredBoundsAndRejectTruncation() => MatroskaHeaderTestInput.VerifyBoundsAndTruncationAsync();

        [Fact]
        public Task CanceledOrFailedReadsReleaseStorageAndBorrowTheirSource() => MatroskaHeaderTestInput.VerifyCancellationAndQuotaAsync();

        [Fact]
        public Task IndependentlyMuxedDocumentHeaderIsReadWithoutReadingTheSegment() => MatroskaHeaderTestInput.VerifyMuxedFileAsync();
    }
}
