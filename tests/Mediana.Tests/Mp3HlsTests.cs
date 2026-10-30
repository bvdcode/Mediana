// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class Mp3HlsTests
    {
        [Theory]
        [InlineData("avc-mp3-44.mp4")]
        [InlineData("avc-mp3-22.mp4")]
        public async Task HlsCopiesAllPacketsAndSupportsRepeatableOutOfOrderSegments(string file)
        {
            await Mp3HlsTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), file);
        }

    }
}
