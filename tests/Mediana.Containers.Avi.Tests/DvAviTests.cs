// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Containers.Avi.Tests
{
    public class DvAviTests
    {
        [Fact]
        public Task PalFrameAndNonAlignedSeekMatchReference() =>
            DvAviTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Dv"));
    }
}
