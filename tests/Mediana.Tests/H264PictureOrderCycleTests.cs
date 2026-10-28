using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264PictureOrderCycleTests
    {
        [Fact]
        public void PreservesTypeOneOffsetsAndFrameNumberGapSignalling()
        {
            byte[] nal = [0x67, .. H264TestBits.Rbsp("01000010 00000000 00001010 1 1 010 0 00101 010 011 00100 00110 010 1 1 1 1 1 0 0")];
            H264SequenceParameterSet first = H264SequenceParser.Parse(nal, new H264DecoderOptions());
            H264SequenceParameterSet second = H264SequenceParser.Parse(nal, new H264DecoderOptions());
            Assert.Equal(1, first.PictureOrderType);
            Assert.Equal(-2, first.NonReferencePictureOrderOffset);
            Assert.Equal(1, first.TopToBottomPictureOrderOffset);
            Assert.Equal(2, first.PictureOrderCycle.Count);
            Assert.Equal(2, first.PictureOrderCycle[0]);
            Assert.Equal(3, first.PictureOrderCycle[1]);
            Assert.True(first.FrameNumberGapsAllowed);
            Assert.Equal(first, second);
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
        }

        [Fact]
        public void CycleValuesOwnTheirStorageAndCompareByContents()
        {
            int[] values = [2, 3];
            H264PictureOrderCycle cycle = new(values);
            values[0] = 99;
            Assert.Equal(new H264PictureOrderCycle([2, 3]), cycle);
            Assert.NotEqual(new H264PictureOrderCycle([2, 4]), cycle);
            Assert.Equal(2, cycle[0]);
        }
    }
}
