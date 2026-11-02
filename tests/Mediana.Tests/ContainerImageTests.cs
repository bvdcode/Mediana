// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class ContainerImageTests
    {
        [Fact]
        public Task RealContainersReturnOriginalOwnedImagesWithoutMovingPackets() =>
            ContainerImageTestInput.VerifyRealFilesAsync(Path.Combine(AppContext.BaseDirectory, "Data"));

        [Fact]
        public Task ExplicitSourcePriorityUsesContentAndSkipsExternalAndDeferredNonimages() => ContainerImageTestInput.VerifyPriorityAsync();

        [Fact]
        public Task SearchLimitsFailExplicitlyAndReleaseAllTemporaryStorage() => ContainerImageTestInput.VerifyLimitsAsync();

        [Fact]
        public Task EmptySearchDoesNotRequireAProbeAllocationAndRejectsDisposedSessions() => ContainerImageTestInput.VerifyEmptyAsync();

        [Fact]
        public Task ARecognizedCorruptImageIsNotSilentlyReplacedByTheNextImage() =>
            ContainerImageTestInput.VerifyRecognizedFailureAsync(Path.Combine(AppContext.BaseDirectory, "Data"));

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public Task CancellationReleasesProbeOrSelectedPayload(bool afterPayload) => ContainerImageTestInput.VerifyCancellationAsync(afterPayload);

        [Fact]
        public void SourcePriorityIsAnImmutableValidatedSnapshot()
        {
            ContainerImageSource[] order = [ContainerImageSource.Attachment, ContainerImageSource.Picture];
            ContainerImageSearchOptions options = new(order);
            order[0] = ContainerImageSource.Picture;
            Assert.Equal(ContainerImageSource.Attachment, options.Sources[0]);
            Assert.Throws<ArgumentNullException>(() => new ContainerImageSearchOptions(null!));
            Assert.Throws<ArgumentException>(() => new ContainerImageSearchOptions([]));
            Assert.Throws<ArgumentException>(() => new ContainerImageSearchOptions([ContainerImageSource.Picture, ContainerImageSource.Picture]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContainerImageSearchOptions([(ContainerImageSource)99]));
        }
    }
}
