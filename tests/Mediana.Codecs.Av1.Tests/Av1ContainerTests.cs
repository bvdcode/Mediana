// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1ContainerTests
    {
        [Fact]
        public Task FinalObusWithoutSizesPreserveAllPicturesAndRandomAccess() =>
            Mediana.Tests.Av1ContainerTestInput.VerifyImplicitFinalSizesAsync(Path.Combine(AppContext.BaseDirectory, "Data"));

        [Theory]
        [InlineData("mp4")]
        [InlineData("mkv")]
        public Task PublicDecodingAndRepeatedIndexedSeekingMatchIndependentPixels(string extension) =>
            Mediana.Tests.Av1ContainerTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"), extension);
    }
}
