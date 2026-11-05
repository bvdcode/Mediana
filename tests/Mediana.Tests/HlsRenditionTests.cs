// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class HlsRenditionTests
    {
        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data", "Mp4");
        [Fact]
        public Task PublicPipelineCreatesAlignedVariantsAndOwnsConsumedFrames() => HlsRenditionTestInput.VerifyAsync(Root);
        [Fact]
        public Task PublicPipelineRoutesSeparateAudioProfiles() => HlsRenditionTestInput.VerifySeparateAudioAsync(Root);
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public Task FailureAndCancellationReleaseWritersAndBorrowInput(bool cancel) => HlsRenditionTestInput.VerifyFailureAsync(Root, cancel);
    }
}
