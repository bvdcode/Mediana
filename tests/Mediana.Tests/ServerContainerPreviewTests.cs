// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class ServerContainerPreviewTests
    {
        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data");

        [Fact]
        public Task RealAudioAndVideoCoversProduceCompletePreviewsWithoutVideoDecoders() =>
            ServerContainerPreviewTestInput.VerifyRealCoversAsync(Root);

        [Fact]
        public Task ExplicitAttachmentPriorityUsesSignaturesAndAppliesEveryExifOrientationOnce() =>
            ServerContainerPreviewTestInput.VerifySelectionAsync(Root);

        [Fact]
        public Task RecognizedCorruptImageFailsWithoutTryingAnotherImageOrVideo() =>
            ServerContainerPreviewTestInput.VerifyRecognizedFailureAsync(Root);

        [Fact]
        public void UnknownOrShortDurationSelectsStartAndKnownDurationSelectsMidpoint() =>
            ServerContainerPreviewTestInput.VerifyTargets();
    }
}
