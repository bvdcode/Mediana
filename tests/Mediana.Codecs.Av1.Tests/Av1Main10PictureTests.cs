// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1Main10PictureTests
    {
        [Fact]
        public void MatchesIndependentNativeTenBitPicturesAcrossInterFiltersTilesAndGrain() =>
            Av1Main10TestInput.VerifyPictures(Root);

        [Fact]
        public void HeldTenBitFramesSurviveReferenceUpdatesResetCancellationAndDisposal() =>
            Av1Main10TestInput.VerifyOwnership(Root);

        [Theory]
        [InlineData("mp4")]
        [InlineData("mkv")]
        public Task ContainersPreserveExactTenBitSamplesAcrossRepeatedSeeks(string extension) =>
            Av1Main10ContainerTestInput.VerifyAsync(Root, extension);

        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data");
    }
}
