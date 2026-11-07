// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Vp9.Tests
{
    public class Vp9ContainerTests
    {
        [Theory]
        [InlineData("main8")]
        [InlineData("resize")]
        [InlineData("main10")]
        [InlineData("main12")]
        [InlineData("high8")]
        [InlineData("high12")]
        [InlineData("chroma422")]
        [InlineData("chroma440")]
        [InlineData("rgb")]
        [InlineData("lossless")]
        [InlineData("resilient")]
        [InlineData("tiles")]
        [InlineData("altref")]
        [InlineData("adaptive")]
        public async Task WebmDecodePreviewAndIndexedSeekMatchIndependentPictures(string name)
            => await Vp9ContainerTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"), name);
    }
}
