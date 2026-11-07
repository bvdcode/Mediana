// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Hevc.Tests
{
    public class HevcSeekTests
    {
        [Theory]
        [InlineData("main8", 12, 12, ".mp4")]
        [InlineData("main10", 12, 12, ".mp4")]
        [InlineData("main8", 12, 12, ".mkv")]
        [InlineData("main10", 12, 12, ".mkv")]
        [InlineData("gop8", 48, 12, ".mp4")]
        [InlineData("gop10", 48, 12, ".mp4")]
        [InlineData("slices8", 12, 6, ".mp4")]
        [InlineData("predictive10", 20, 10, ".mp4")]
        public async Task RandomOrderSeeksAndRestartsPreserveExactPicturesAndOwnership(string name, int count, int rate, string extension)
        {
            await HevcSeekTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"), name, count, rate, extension);
        }
    }
}
