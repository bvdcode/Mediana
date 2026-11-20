// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264FieldOrderTests
    {
        [Fact]
        public void TypeZeroWrapsAcrossSeparateFieldsWithoutInventingTheOtherFieldCount()
        {
            H264PictureOrderState state = new();
            int[] lsb = [0, 1, 6, 7, 14, 15, 0, 1];
            int[] expected = [0, 1, 6, 7, 14, 15, 16, 17];
            for (int index = 0; index < lsb.Length; index++)
            {
                H264SliceHeader field = Header(0, index / 2, bottom: index % 2 != 0, idr: index == 0) with
                {
                    PictureOrderLsb = lsb[index]
                };
                state = state.Advance(field);
                Assert.Equal(expected[index], state.Current.Value);
                Assert.Equal(index % 2 == 0 ? 0 : expected[index], state.Current.Bottom);
                Assert.Equal(index % 2 == 0 ? expected[index] : 0, state.Current.Top);
            }
        }

        [Fact]
        public void TypeOneAddsTheTopToBottomOffsetOnlyToTheBottomField()
        {
            H264PictureOrderState state = new H264PictureOrderState().Advance(Header(1, 0, idr: true));
            Assert.Equal(0, state.Current.Value);
            state = state.Advance(Header(1, 0, bottom: true));
            Assert.Equal(1, state.Current.Value);
            state = state.Advance(Header(1, 1) with { DeltaPictureOrder0 = 2 });
            Assert.Equal(4, state.Current.Value);
            state = state.Advance(Header(1, 1, bottom: true) with { DeltaPictureOrder0 = -1 });
            Assert.Equal(2, state.Current.Value);
            state = state.Advance(Header(1, 2));
            Assert.Equal(5, state.Current.Value);
            state = state.Advance(Header(1, 2, bottom: true));
            Assert.Equal(6, state.Current.Value);
        }

        [Fact]
        public void TypeTwoAllowsEqualCountsOnlyForComplementaryFieldsAndHandlesFrameNumberWrap()
        {
            H264PictureOrderState state = new H264PictureOrderState().Advance(Header(2, 0, idr: true));
            state = state.Advance(Header(2, 0, bottom: true));
            for (int frame = 1; frame < 34; frame++)
            {
                state = state.Advance(Header(2, frame % 16) with { ReferenceIndicator = 0 });
                Assert.Equal(frame * 2 - 1, state.Current.Value);
                state = state.Advance(Header(2, frame % 16, bottom: true) with { ReferenceIndicator = 0 });
                Assert.Equal(frame * 2 - 1, state.Current.Value);
                H264PictureOrderState paired = state;
                Assert.Throws<InvalidDataException>(() => paired.Advance(Header(2, frame % 16) with { ReferenceIndicator = 0 }));
                state = state.Advance(Header(2, frame % 16));
                Assert.Equal(frame * 2, state.Current.Value);
                state = state.Advance(Header(2, frame % 16, bottom: true));
                Assert.Equal(frame * 2, state.Current.Value);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ResetInEitherFieldNormalizesOnlyItsOwnOrderAndAllowsTheNextReferenceField(bool bottom)
        {
            H264PictureOrderState state = new H264PictureOrderState().Advance(Header(0, 0, idr: true));
            state = state.Advance(Header(0, 0, bottom: true) with { PictureOrderLsb = 1 });
            state = state.Advance(Header(0, 1, bottom: bottom) with
            {
                PictureOrderLsb = 6,
                AdaptiveMarking = true,
                MemoryOperations = [new(5)]
            });
            Assert.Equal(0, state.Current.Value);
            Assert.Equal(0, state.Current.Top);
            Assert.Equal(0, state.Current.Bottom);
            state = state.Advance(Header(0, 0, bottom: !bottom) with { PictureOrderLsb = 2 });
            Assert.Equal(2, state.Current.Value);
            H264PictureOrderState paired = state;
            Assert.Throws<InvalidDataException>(() => paired.Advance(Header(0, 0, bottom: bottom) with { PictureOrderLsb = 3 }));
        }

        [Fact]
        public void ReferenceFrameNumberCannotBeReusedByTheSameParityOrAcrossAnInterveningPicture()
        {
            H264PictureOrderState first = new H264PictureOrderState().Advance(Header(0, 0, idr: true));
            Assert.Throws<InvalidDataException>(() => first.Advance(Header(0, 0) with { PictureOrderLsb = 1 }));
            H264PictureOrderState interrupted = first.Advance(Header(0, 1) with { ReferenceIndicator = 0, PictureOrderLsb = 2 });
            Assert.Throws<InvalidDataException>(() => interrupted.Advance(Header(0, 0, bottom: true) with { PictureOrderLsb = 1 }));
            Assert.Equal(0, first.Current.Value);
        }

        [Fact]
        public void BottomFieldIdrUsesItsOwnOrderForTheZeroOriginCheck()
        {
            H264SliceHeader field = Header(1, 0, bottom: true, idr: true) with { DeltaPictureOrder0 = -1 };
            H264PictureOrderState state = new H264PictureOrderState().Advance(field);
            Assert.Equal(0, state.Current.Value);
            Assert.Throws<InvalidDataException>(() => new H264PictureOrderState().Advance(Header(1, 0, bottom: true, idr: true)));
        }

        private static H264SliceHeader Header(int type, int frameNumber, bool bottom = false, bool idr = false) =>
            new(new H264SequenceParameterSet
            {
                FrameNumberBits = 4,
                PictureOrderBits = 4,
                PictureOrderType = type,
                PictureOrderCycle = new H264PictureOrderCycle([2, 3]),
                NonReferencePictureOrderOffset = -1,
                TopToBottomPictureOrderOffset = type == 1 ? 1 : 0,
                WidthInMbs = 6,
                HeightInMapUnits = 3
            }, new H264PictureParameterSet(0, 0, false, false, 26, 0, 0, false, false, false, false, 1, 1, false, 0),
                bottom ? H264PictureStructure.BottomField : H264PictureStructure.TopField)
            {
                Kind = idr ? H264SliceKind.I : H264SliceKind.P,
                IsIdr = idr,
                ReferenceIndicator = 1,
                FrameNumber = frameNumber
            };
    }
}
