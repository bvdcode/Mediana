// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class ServerAudioPreviewTests
    {
        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data");

        [Fact]
        public Task RecognizedCoversAvoidAudioDecodingAndReleaseAllStorage() =>
            ServerAudioPreviewTestInput.VerifyCoversAsync(Root);

        [Fact]
        public Task MissingCoverTransfersTheSameContainerToCompleteWaveformAnalysis() =>
            ServerAudioPreviewTestInput.VerifyWaveformsAsync(Root);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public Task RecognizedImageFailureOrCancellationDoesNotTryAnotherSource(bool cancel) =>
            ServerAudioPreviewTestInput.VerifyRecognizedFailureAsync(Root, cancel);
    }
}
