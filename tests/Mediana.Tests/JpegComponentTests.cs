// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class JpegComponentTests
    {
        [Fact]
        public Task AllIndependentPixelsMatchThroughTheComponentApi() => JpegComponentTestInput.VerifyCorpusAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg"));
        [Fact]
        public void ComponentsOwnStorageAndWriteWithoutAllocatingOutput() => JpegComponentTestInput.VerifyOwnership();
        [Fact]
        public void EmbeddingMetadataExposesAdobeAndLeavesPresentationToTheContainer() => JpegComponentTestInput.VerifyMetadata();
        [Fact]
        public void TruncationCancellationAndBudgetFailuresReleaseComponentStorage() => JpegComponentTestInput.VerifyFailures();
    }
}
