// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Vp9.Tests
{
    public class Vp9KeyPictureTests
    {
        [Theory]
        [InlineData("main8", false)]
        [InlineData("main8", true)]
        [InlineData("main10", false)]
        [InlineData("main10", true)]
        [InlineData("high8", false)]
        [InlineData("high8", true)]
        [InlineData("high12", false)]
        [InlineData("high12", true)]
        [InlineData("lossless", false)]
        [InlineData("lossless", true)]
        [InlineData("resilient", false)]
        [InlineData("resilient", true)]
        [InlineData("tiles", false)]
        [InlineData("tiles", true)]
        [InlineData("altref", false)]
        [InlineData("altref", true)]
        public async Task OpeningPictureMatchesIndependentReconstruction(string name, bool filtered)
        {
            await Vp9KeyPictureTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"), name, filtered);
        }
    }
}
