// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Hevc.Tests
{
    public class HevcPictureTests
    {
        [Theory]
        [InlineData("main8", 12)]
        [InlineData("main10", 12)]
        [InlineData("weighted8", 24)]
        [InlineData("weighted10", 24)]
        public async Task CompleteMainAndMain10SequencesMatchEveryIndependentLumaAndChromaSample(string name, int count)
        {
            Assert.Equal(count, await HevcPictureTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"), name));
        }
    }
}
