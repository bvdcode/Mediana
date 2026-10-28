// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264CabacContextTests
    {
        private static readonly byte[] LeastProbableStates =
        [
            0, 0, 1, 2, 2, 4, 4, 5, 6, 7, 8, 9, 9, 11, 11, 12,
            13, 13, 15, 15, 16, 16, 18, 18, 19, 19, 21, 21, 22, 22, 23, 24,
            24, 25, 26, 26, 27, 27, 28, 29, 29, 30, 30, 30, 31, 32, 32, 33,
            33, 33, 34, 34, 35, 35, 35, 36, 36, 36, 37, 37, 37, 38, 38, 63
        ];

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AllStatesFollowNormativeTransitionsWithoutChangingCopies(bool symbol)
        {
            for (byte state = 0; state < 64; state++)
            {
                H264CabacContext context = new(state, symbol);
                H264CabacContext mostProbable = context.NextMostProbable();
                H264CabacContext leastProbable = context.NextLeastProbable();
                Assert.Equal(state, context.State);
                Assert.Equal(symbol, context.MostProbableSymbol);
                Assert.Equal(state < 62 ? state + 1 : state, mostProbable.State);
                Assert.Equal(symbol, mostProbable.MostProbableSymbol);
                Assert.Equal(LeastProbableStates[state], leastProbable.State);
                Assert.Equal(symbol != (state == 0), leastProbable.MostProbableSymbol);
            }
            H264CabacContext zero = default;
            Assert.Equal(0, zero.State);
            Assert.False(zero.MostProbableSymbol);
        }

        [Theory]
        [InlineData(64)]
        [InlineData(127)]
        [InlineData(128)]
        [InlineData(255)]
        public void RejectsInvalidProbabilityStates(byte state)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new H264CabacContext(state, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => new H264CabacContext(state, true));
        }

        [Theory]
        [InlineData(20, -15, 0, 62, false)]
        [InlineData(20, -15, 26, 46, false)]
        [InlineData(20, -15, 51, 15, false)]
        [InlineData(-28, 127, 0, 62, true)]
        [InlineData(-28, 127, 26, 17, true)]
        [InlineData(-28, 127, 51, 26, false)]
        [InlineData(0, 63, 26, 0, false)]
        [InlineData(0, 64, 26, 0, true)]
        public void InitializesSignedH264Coefficients(sbyte slope, sbyte intercept, int qp, byte state, bool symbol)
        {
            H264CabacContext context = H264CabacContext.Initialize(slope, intercept, qp);
            Assert.Equal(state, context.State);
            Assert.Equal(symbol, context.MostProbableSymbol);
        }

        [Fact]
        public void ClipsTheSliceQuantizerBeforeMultiplyingAndTheStateAfterward()
        {
            Assert.Equal(H264CabacContext.Initialize(-28, 127, 0), H264CabacContext.Initialize(-28, 127, int.MinValue));
            Assert.Equal(H264CabacContext.Initialize(-28, 127, 51), H264CabacContext.Initialize(-28, 127, int.MaxValue));
            H264CabacContext lower = H264CabacContext.Initialize(-128, -128, 51);
            H264CabacContext upper = H264CabacContext.Initialize(127, 127, 51);
            Assert.Equal(62, lower.State);
            Assert.False(lower.MostProbableSymbol);
            Assert.Equal(62, upper.State);
            Assert.True(upper.MostProbableSymbol);
        }
    }
}
