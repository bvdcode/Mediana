// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;

namespace Mediana.Tests
{
    public class AudioWindowTests
    {
        [Theory]
        [InlineData(6L, 17L, 17)]
        [InlineData(6L, 100L, 34)]
        [InlineData(40L, 5L, 0)]
        [InlineData(41L, null, 0)]
        [InlineData(100L, 0L, 0)]
        [InlineData(6L, null, 34)]
        [InlineData(0L, 40L, 40)]
        [InlineData(39L, 1L, 1)]
        public async Task WindowSelectsOnlyAvailableSamplesWithIndependentStorage(long skip, long? maximumCount, int expectedCount)
        {
            foreach (AudioStorageLayout layout in new[] { AudioStorageLayout.Interleaved, AudioStorageLayout.Planar })
            {
                await AudioWindowTestInput.VerifyAsync(layout, skip, maximumCount, expectedCount, false);
                await AudioWindowTestInput.VerifyAsync(layout, skip, maximumCount, expectedCount, true);
            }
        }

        [Fact]
        public async Task WindowPropagatesSourceFailureAndBecomesTerminal()
        {
            await AudioWindowTestInput.VerifySourceFailureAsync();
        }
    }
}
