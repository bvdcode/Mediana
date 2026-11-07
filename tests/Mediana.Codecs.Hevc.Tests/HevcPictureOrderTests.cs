// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc.Tests
{
    public class HevcPictureOrderTests
    {
        [Fact]
        public void WrapsForwardAndUsesAsymmetricHalfRangeBoundaries()
        {
            HevcPictureOrderCounter order = new(4);
            Assert.Equal(0, order.Derive(19, 0, 0, true));
            Assert.Equal(7, order.Derive(1, 0, 7, false));
            Assert.Equal(14, order.Derive(1, 0, 14, false));
            Assert.Equal(18, order.Derive(1, 0, 2, false));
            order.Reset();
            order.Derive(19, 0, 0, true);
            Assert.Equal(8, order.Derive(1, 0, 8, false));
            Assert.Equal(16, order.Derive(1, 0, 0, false));
        }

        [Fact]
        public void DerivesNegativeLeadingOrderAndResetsOnlyWhenCraStartsANewSequence()
        {
            HevcPictureOrderCounter order = new(4);
            order.Derive(19, 0, 0, true);
            Assert.Equal(-1, order.Derive(9, 0, 15, false));
            order.Derive(21, 0, 14, true);
            Assert.Equal(18, order.Derive(21, 0, 2, false));
            Assert.Equal(2, order.Derive(21, 0, 2, true));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(2, 0)]
        [InlineData(4, 0)]
        [InlineData(6, 0)]
        [InlineData(7, 0)]
        [InlineData(8, 0)]
        [InlineData(9, 0)]
        [InlineData(1, 1)]
        public void LeadingNonReferenceAndHigherTemporalPicturesDoNotReplaceTidZeroHistory(int type, int temporalId)
        {
            HevcPictureOrderCounter order = new(4);
            order.Derive(21, 0, 14, true);
            Assert.Equal(18, order.Derive(1, 0, 2, false));
            Assert.Equal(26, order.Derive(type, temporalId, 10, false));
            Assert.Equal(17, order.Derive(1, 0, 1, false));
        }

        [Theory]
        [InlineData(19, 0, 1, true)]
        [InlineData(19, 0, 0, false)]
        [InlineData(21, 1, 0, true)]
        [InlineData(1, 0, 0, false)]
        [InlineData(1, 0, 0, true)]
        [InlineData(10, 0, 0, false)]
        public void RejectsInvalidDeclarationsAndMissingInitialReference(int type, int temporalId, int lsb, bool noRasl)
        {
            HevcPictureOrderCounter order = new(4);
            Assert.Throws<InvalidDataException>(() => order.Derive(type, temporalId, lsb, noRasl));
            Assert.Equal(0, order.Derive(19, 0, 0, true));
        }
    }
}
