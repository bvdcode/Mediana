// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1IntraBlockHeaderTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void IntraCopyExcludesDeltaFilterSyntaxWithoutConsumingFollowingBits(bool intrabc)
        {
            Av1BitReader bits = new([0b11010110]);
            Av1BlockDeltaParameters deltas = Av1BlockDeltaParameters.Read(ref bits, 128, intrabc);
            Assert.True(deltas.QuantizerPresent);
            Assert.Equal(2, deltas.QuantizerShift);
            Assert.Equal(!intrabc, deltas.FilterPresent);
            if (intrabc)
            {
                Assert.Equal(0, deltas.FilterShift);
                Assert.False(deltas.MultiFilter);
                Assert.True(bits.Flag());
            }
            else
            {
                Assert.Equal(1, deltas.FilterShift);
                Assert.True(deltas.MultiFilter);
                Assert.False(bits.Flag());
            }
        }
    }
}
