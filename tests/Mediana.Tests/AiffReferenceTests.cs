// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class AiffReferenceTests
    {
        [Theory]
        [InlineData("s8-mono")]
        [InlineData("s16-stereo")]
        [InlineData("s24-stereo")]
        [InlineData("s32-stereo")]
        [InlineData("f32-stereo")]
        [InlineData("f64-stereo")]
        [InlineData("s16le-stereo")]
        public Task EverySampleAndTimestampMatchesTheIndependentReference(string name) =>
            AiffPcmReferenceTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aiff"), name);
    }
}
