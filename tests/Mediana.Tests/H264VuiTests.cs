using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264VuiTests
    {
        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(1, 1, 1)]
        [InlineData(2, 12, 11)]
        [InlineData(3, 10, 11)]
        [InlineData(4, 16, 11)]
        [InlineData(5, 40, 33)]
        [InlineData(6, 24, 11)]
        [InlineData(7, 20, 11)]
        [InlineData(8, 32, 11)]
        [InlineData(9, 80, 33)]
        [InlineData(10, 18, 11)]
        [InlineData(11, 15, 11)]
        [InlineData(12, 64, 33)]
        [InlineData(13, 160, 99)]
        [InlineData(14, 4, 3)]
        [InlineData(15, 3, 2)]
        [InlineData(16, 2, 1)]
        public void PreservesStandardSampleAspectRatios(int identifier, int width, int height)
        {
            H264VuiParameters value = Read("1 " + Convert.ToString(identifier, 2).PadLeft(8, '0') + " 00000000");
            Assert.Equal(width, value.AspectRatioWidth);
            Assert.Equal(height, value.AspectRatioHeight);
        }

        [Theory]
        [InlineData(123, 77)]
        [InlineData(0, 77)]
        [InlineData(123, 0)]
        public void PreservesExtendedAndUnspecifiedAspectRatios(int width, int height)
        {
            H264VuiParameters value = Read("1 11111111 " + Convert.ToString(width, 2).PadLeft(16, '0') + " " +
                Convert.ToString(height, 2).PadLeft(16, '0') + " 00000000");
            Assert.Equal(width, value.AspectRatioWidth);
            Assert.Equal(height, value.AspectRatioHeight);
        }

        [Fact]
        public void PreservesColorTimingChromaLocationAndBufferingWithoutConversion()
        {
            string content = "0 1 0 1 101 1 1 00001001 00010000 00001001 1 " + H264TestBits.Ue(2) + H264TestBits.Ue(3) +
                " 1 00000000000000000000000000000001 00000000000000000000000000110010 0 0 0 1 1 1 " +
                H264TestBits.Ue(0) + H264TestBits.Ue(0) + H264TestBits.Ue(10) + H264TestBits.Ue(9) + H264TestBits.Ue(2) + H264TestBits.Ue(4);
            H264VuiParameters value = Read(content, 3);
            Assert.False(value.OverscanAppropriate);
            Assert.Equal(5, value.VideoFormat);
            Assert.True(value.FullRange);
            Assert.Equal(9, value.ColorPrimaries);
            Assert.Equal(16, value.TransferCharacteristics);
            Assert.Equal(9, value.MatrixCoefficients);
            Assert.Equal(2, value.ChromaLocationTop);
            Assert.Equal(3, value.ChromaLocationBottom);
            Assert.Equal(1u, value.UnitsInTick);
            Assert.Equal(50u, value.TimeScale);
            Assert.False(value.FixedFrameRate);
            Assert.True(value.PictureStructurePresent);
            Assert.Equal(10, value.MaxMotionLengthHorizontal);
            Assert.Equal(9, value.MaxMotionLengthVertical);
            Assert.Equal(2, value.MaxReorderFrames);
            Assert.Equal(4, value.MaxDecodedFrameBuffering);
        }

        [Fact]
        public void AbsentSyntaxUsesSpecifiedDefaultsAndHrdSyntaxKeepsAlignment()
        {
            Assert.Equal(new H264VuiParameters(), Read("000000000"));
            string hrd = "1 00000000 111 00000000000000000000";
            H264VuiParameters value = Read("00000 1 " + hrd + " 1 " + hrd + " 1 1 0");
            Assert.True(value.PictureStructurePresent);
        }

        [Theory]
        [InlineData("1 00010001")]
        [InlineData("001 110")]
        [InlineData("0001 00111")]
        [InlineData("00001 00000000000000000000000000000000 1")]
        public void RejectsReservedOrInvalidValues(string content) =>
            Assert.Throws<InvalidDataException>(() => Read(content));

        [Theory]
        [InlineData(16, 15, 0, 3, 3)]
        [InlineData(15, 16, 0, 3, 3)]
        [InlineData(15, 15, 4, 3, 3)]
        [InlineData(15, 15, 0, 2, 3)]
        [InlineData(15, 15, 0, 17, 3)]
        public void RejectsInconsistentOrUnboundedBuffering(int horizontal, int vertical, int reorder, int buffering, int references)
        {
            string content = "00000000 1 1 " + H264TestBits.Ue(0) + H264TestBits.Ue(0) + H264TestBits.Ue(horizontal) +
                H264TestBits.Ue(vertical) + H264TestBits.Ue(reorder) + H264TestBits.Ue(buffering);
            Assert.Throws<InvalidDataException>(() => Read(content, references));
        }

        private static H264VuiParameters Read(string content, int references = 0)
        {
            H264BitReader bits = new(H264TestBits.Rbsp(content));
            H264VuiParameters result = H264VuiParser.Parse(ref bits, references);
            bits.ReadTrailingBits();
            return result;
        }
    }
}
