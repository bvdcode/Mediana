// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Xunit;

namespace Mediana.Tests
{
    public class TgsReferencePixelTests
    {
        [Fact]
        public void ComparisonAllowsOneRgbLevelButRequiresExactAlphaAndDimensions()
        {
            byte[] reference = [100, 100, 100, 100];
            TgsReferenceTestInput.VerifyPixels([99, 100, 101, 100], reference, "rounding");
            Assert.Throws<InvalidDataException>(() => TgsReferenceTestInput.VerifyPixels([98, 100, 100, 100], reference, "red"));
            Assert.Throws<InvalidDataException>(() => TgsReferenceTestInput.VerifyPixels([100, 102, 100, 100], reference, "green"));
            Assert.Throws<InvalidDataException>(() => TgsReferenceTestInput.VerifyPixels([100, 100, 98, 100], reference, "blue"));
            Assert.Throws<InvalidDataException>(() => TgsReferenceTestInput.VerifyPixels([100, 100, 100, 101], reference, "alpha"));
            Assert.Throws<InvalidDataException>(() => TgsReferenceTestInput.VerifyPixels([], reference, "size"));
            Assert.Throws<InvalidDataException>(() => TgsReferenceTestInput.VerifyPixels([100], [100], "incomplete pixel"));
        }
    }
}
