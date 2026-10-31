// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class H264LevelBurstTests
    {
        [Fact]
        public async Task FixedQuantizerAllowsAnInitialPictureAboveTheInstantaneousRate() =>
            await H264LevelBurstTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"));
    }
}
