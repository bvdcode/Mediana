using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264PictureOrderTests
    {
        [Fact]
        public void TypeZeroUsesOnlyReferenceHistoryForForwardAndBackwardLsbWraps()
        {
            H264SliceHeader header = Header(0, 0, idr: true);
            H264PictureOrderState state = new H264PictureOrderState().Advance(header);
            state = state.Advance(Header(0, 1) with { PictureOrderLsb = 6 });
            Assert.Equal(6, state.Current.Value);
            state = state.Advance(Header(0, 2) with { PictureOrderLsb = 2, ReferenceIndicator = 0 });
            Assert.Equal(2, state.Current.Value);
            state = state.Advance(Header(0, 2) with { PictureOrderLsb = 14 });
            Assert.Equal(14, state.Current.Value);
            state = state.Advance(Header(0, 3) with { PictureOrderLsb = 2 });
            Assert.Equal(18, state.Current.Value);
            state = state.Advance(Header(0, 4) with { PictureOrderLsb = 14, ReferenceIndicator = 0 });
            Assert.Equal(14, state.Current.Value);
        }

        [Fact]
        public void TypeOneCombinesCycleOffsetsDeltasAndNonReferenceOffsets()
        {
            H264PictureOrderState state = new H264PictureOrderState().Advance(Header(1, 0, idr: true));
            state = state.Advance(Header(1, 1) with { DeltaPictureOrder0 = 1, DeltaPictureOrder1 = -2 });
            Assert.Equal(new H264PictureOrder(3, 2), state.Current);
            state = state.Advance(Header(1, 2));
            Assert.Equal(new H264PictureOrder(5, 6), state.Current);
            state = state.Advance(Header(1, 3) with { ReferenceIndicator = 0 });
            Assert.Equal(new H264PictureOrder(4, 5), state.Current);
            state = state.Advance(Header(1, 3));
            Assert.Equal(new H264PictureOrder(7, 8), state.Current);
        }

        [Fact]
        public void TypeTwoPreservesNonReferenceOrderAndCyclicFrameNumbers()
        {
            H264PictureOrderState state = new H264PictureOrderState().Advance(Header(2, 0, idr: true));
            for (int index = 1; index < 34; index++)
            {
                state = state.Advance(Header(2, index % 16) with { ReferenceIndicator = 0 });
                Assert.Equal(index * 2 - 1, state.Current.Value);
                state = state.Advance(Header(2, index % 16));
                Assert.Equal(index * 2, state.Current.Value);
            }
        }

        [Fact]
        public void ReferenceResetNormalizesBothFieldCountsAndTheNextLsbPredictor()
        {
            H264PictureOrderState state = new H264PictureOrderState().Advance(Header(0, 0, idr: true));
            state = state.Advance(Header(0, 1) with
            {
                PictureOrderLsb = 6,
                DeltaPictureOrderBottom = -2,
                AdaptiveMarking = true,
                MemoryOperations = [new(5)]
            });
            Assert.Equal(new H264PictureOrder(2, 0), state.Current);
            state = state.Advance(Header(0, 1) with { PictureOrderLsb = 4 });
            Assert.Equal(4, state.Current.Value);
            state = state.Advance(Header(0, 0, idr: true));
            Assert.Equal(0, state.Current.Value);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void ReferenceResetRestartsFrameOffsets(int type)
        {
            H264PictureOrderState state = new H264PictureOrderState().Advance(Header(type, 0, idr: true));
            for (int index = 1; index < 18; index++)
            {
                state = state.Advance(Header(type, index % 16));
            }
            state = state.Advance(Header(type, 2) with { AdaptiveMarking = true, MemoryOperations = [new(5)] });
            Assert.Equal(0, state.Current.Value);
            state = state.Advance(Header(type, 1));
            Assert.Equal(2, state.Current.Value);
        }

        [Fact]
        public void InvalidOrderOrGapsLeaveThePreviousStateUsable()
        {
            H264PictureOrderState state = new H264PictureOrderState().Advance(Header(0, 0, idr: true));
            Assert.Throws<InvalidDataException>(() => state.Advance(Header(0, 0)));
            Assert.Throws<InvalidDataException>(() => state.Advance(Header(0, 2)));
            H264SequenceParameterSet gaps = Header(0, 0).Sequence with { FrameNumberGapsAllowed = true };
            H264PictureOrderState allowed = new H264PictureOrderState().Advance(Header(0, 0, idr: true) with { Sequence = gaps });
            Assert.Throws<NotSupportedException>(() => allowed.Advance(Header(0, 2) with { Sequence = gaps }));
            Assert.Throws<InvalidDataException>(() => new H264PictureOrderState().Advance(Header(0, 0, idr: true) with { PictureOrderLsb = 1 }));
            Assert.Equal(0, state.Current.Value);
            state = state.Advance(Header(0, 1) with { PictureOrderLsb = 2 });
            Assert.Equal(2, state.Current.Value);
        }

        [Fact]
        public void TypeOneRejectsSignedOrderOverflow()
        {
            H264SequenceParameterSet sequence = Header(1, 0).Sequence with
            {
                PictureOrderCycle = new H264PictureOrderCycle([int.MaxValue]),
                TopToBottomPictureOrderOffset = 0
            };
            H264PictureOrderState state = new H264PictureOrderState().Advance(Header(1, 0, idr: true) with { Sequence = sequence });
            state = state.Advance(Header(1, 1) with { Sequence = sequence });
            Assert.Equal(int.MaxValue, state.Current.Value);
            Assert.Throws<InvalidDataException>(() => state.Advance(Header(1, 2) with { Sequence = sequence }));
        }

        private static H264SliceHeader Header(int type, int number, bool idr = false) => new(new H264SequenceParameterSet
        {
            FrameNumberBits = 4,
            PictureOrderBits = 4,
            PictureOrderType = type,
            PictureOrderCycle = new H264PictureOrderCycle([2, 3]),
            NonReferencePictureOrderOffset = -1,
            TopToBottomPictureOrderOffset = type == 1 ? 1 : 0,
            FrameMbsOnly = true
        }, new H264PictureParameterSet(0, 0, false, false, 26, 0, 0, true, false, false, false, 1, 1, false, 0))
        {
            Kind = idr ? H264SliceKind.I : H264SliceKind.P,
            IsIdr = idr,
            ReferenceIndicator = 1,
            FrameNumber = number
        };
    }
}
