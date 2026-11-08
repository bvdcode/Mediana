// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1ReferenceOrderingTests
    {
        [Fact]
        public void ShortSignalingOrdersBackwardAndForwardReferences()
        {
            Av1ReferenceIndices references = Av1ReferenceOrdering.Resolve([3, 2, 1, 0, 8, 6, 5, 4], 4, 4, 0, 1);
            ReadOnlySpan<int> expected = [0, 2, 3, 1, 7, 6, 4];
            for (int index = 0; index < expected.Length; index++) { Assert.Equal(expected[index], references[index]); }
        }

        [Fact]
        public void EqualOrderHintsUseNormativeSlotTieBreaking()
        {
            Av1ReferenceIndices references = Av1ReferenceOrdering.Resolve([3, 3, 3, 3, 3, 3, 3, 3], 4, 4, 0, 1);
            ReadOnlySpan<int> expected = [0, 7, 6, 1, 5, 4, 3];
            for (int index = 0; index < expected.Length; index++) { Assert.Equal(expected[index], references[index]); }
        }

        [Theory]
        [InlineData(0, 15, 1)]
        [InlineData(15, 0, -1)]
        [InlineData(8, 0, -8)]
        [InlineData(7, 0, 7)]
        public void RelativeOrderUsesSignedModularDistance(int first, int second, int expected)
        {
            Assert.Equal(expected, Av1ReferenceOrdering.Distance(first, second, 4));
            Assert.Equal(0, Av1ReferenceOrdering.Distance(first, second, 0));
        }

        [Fact]
        public void ShortSignalingRejectsFutureLastOrGoldenReferences()
        {
            Assert.Throws<InvalidDataException>(() => Av1ReferenceOrdering.Resolve([4, 3, 3, 3, 3, 3, 3, 3], 4, 4, 0, 1));
        }
    }
}
