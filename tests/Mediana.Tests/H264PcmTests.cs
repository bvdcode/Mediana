using System.Text;
using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264PcmTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PcmPreservesAllSamplesAndChecksAlignment(bool invalidAlignment)
        {
            H264SequenceParameterSet sequence = new()
            {
                Id = 0,
                Profile = 66,
                FrameNumberBits = 4,
                PictureOrderType = 2,
                WidthInMbs = 1,
                HeightInMapUnits = 1,
                FrameMbsOnly = true,
                Width = 16,
                Height = 16
            };
            H264PictureParameterSet picture = new(Id: 0, SequenceId: 0, Cabac: false,
                BottomFieldPictureOrderPresent: false, InitialQp: 26, ChromaQpOffset: 0, SecondChromaQpOffset: 0,
                DeblockingControlPresent: true, ConstrainedIntraPrediction: false, RedundantPictureCountPresent: false,
                Transform8x8: false, DefaultReferencesL0: 1, DefaultReferencesL1: 1, WeightedPrediction: false, WeightedBiPrediction: 0);
            H264Configuration configuration = new(4, new Dictionary<int, H264SequenceParameterSet> { [0] = sequence },
                new Dictionary<int, H264PictureParameterSet> { [0] = picture });
            // IDR I slice, QP 26, deblocking disabled, I_PCM macroblock and seven alignment bits.
            StringBuilder syntax = new("1011100001001010000011010");
            syntax.Append(invalidAlignment ? "1000000" : "0000000");
            byte[] expected = new byte[384];
            for (int index = 0; index < expected.Length; index++)
            {
                expected[index] = (byte)(index % 8 < 4 ? 0 : index % 256);
                syntax.Append(Convert.ToString(expected[index], 2).PadLeft(8, '0'));
            }
            byte[] nal = [0x65, .. H264TestBits.Rbsp(syntax.ToString())];
            MediaBufferAllocator allocator = new(1024 * 1024);
            if (invalidAlignment)
            {
                Assert.Throws<InvalidDataException>(() => H264IntraDecoder.Decode(nal, configuration, allocator));
            }
            else
            {
                using H264Picture actual = H264IntraDecoder.Decode(nal, configuration, allocator);
                Assert.Equal(expected[..256], actual.Plane(0).ToArray());
                Assert.Equal(expected[256..320], actual.Plane(1).ToArray());
                Assert.Equal(expected[320..], actual.Plane(2).ToArray());
                Assert.All(actual.Counts(0).ToArray(), count => Assert.Equal(16, count));
                Assert.All(actual.Modes.ToArray(), mode => Assert.Equal(2, mode));
                Assert.Equal(0, actual.Quantizers[0]);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
