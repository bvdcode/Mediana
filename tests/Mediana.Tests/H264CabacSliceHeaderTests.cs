using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264CabacSliceHeaderTests
    {
        [Theory]
        [InlineData(0, true, 0, 0, 23, 419)]
        [InlineData(1, false, 1, 6, 25, 377)]
        public async Task RealHighProfileHeadersAndArithmeticStartMatchIndependentTrace(
            int packet, bool idr, int frameNumber, int poc, int qp, int initialOffset)
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-aac.mp4"), packet);
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
            Assert.True(header.Picture.Cabac);
            Assert.True(header.Picture.Transform8x8);
            Assert.Equal(100, header.Sequence.Profile);
            Assert.Equal(idr, header.IsIdr);
            Assert.Equal(idr ? H264SliceKind.I : H264SliceKind.P, header.Kind);
            Assert.Equal(frameNumber, header.FrameNumber);
            Assert.Equal(poc, header.PictureOrderLsb);
            Assert.Equal(qp, header.Qp);
            Assert.Equal(0, header.CabacInitIdc);
            Assert.Equal(0, header.DeblockingIdc);
            Assert.Equal(0, header.AlphaOffset);
            Assert.Equal(0, header.BetaOffset);
            Assert.Equal(63u, bits.ReadBits(6));
            Assert.True(bits.ByteAligned);
            H264CabacDecoder arithmetic = new(ref bits);
            Assert.Equal(initialOffset, arithmetic.Offset);
            Assert.Equal(510, arithmetic.Range);
            MediaBufferAllocator allocator = new(1);
            Assert.Throws<NotSupportedException>(() => Decode(nal, configuration, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task ReadsAllCabacInitializationChoicesBeforeTheQuantizer(int initialization)
        {
            H264Configuration configuration = await SyntheticConfigurationAsync();
            H264BitReader bits = new(SyntheticPayload(configuration, initialization));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, 0x41);
            Assert.Equal(initialization, header.CabacInitIdc);
            Assert.Equal(28, header.Qp);
            Assert.Equal(1, header.DeblockingIdc);
            Assert.Equal(0xaau, bits.ReadBits(8));
        }

        [Fact]
        public async Task RejectsTheReservedCabacInitializationChoice()
        {
            H264Configuration configuration = await SyntheticConfigurationAsync();
            byte[] payload = SyntheticPayload(configuration, 3);
            Assert.Throws<InvalidDataException>(() => ReadPayload(payload, configuration));
        }

        [Theory]
        [InlineData(0, 4)]
        [InlineData(1, 5)]
        public async Task RejectsTruncatedRealCabacHeaders(int packet, int lastTruncatedLength)
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-aac.mp4"), packet);
            for (int length = 1; length <= lastTruncatedLength; length++)
            {
                byte[] truncated = nal[..length];
                Assert.Throws<InvalidDataException>(() => Decode(truncated, configuration, new MediaBufferAllocator(1)));
            }
        }

        private static void ReadPayload(byte[] payload, H264Configuration configuration)
        {
            H264BitReader bits = new(payload);
            H264SliceHeaderReader.Read(ref bits, configuration, 0x41);
        }

        private static void Decode(byte[] nal, H264Configuration configuration, MediaBufferAllocator allocator)
        {
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
            using H264Picture picture = H264PictureDecoder.Decode(ref bits, header, [], allocator);
        }

        private static async Task<H264Configuration> SyntheticConfigurationAsync()
        {
            (H264Configuration configuration, _) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-tail.mov"), 0);
            H264SequenceParameterSet sequence = configuration.Sequences[0] with { PictureOrderType = 2 };
            H264PictureParameterSet picture = configuration.Pictures[0] with
            {
                Cabac = true,
                InitialQp = 26,
                WeightedPrediction = false,
                RedundantPictureCountPresent = false,
                DeblockingControlPresent = true,
                BottomFieldPictureOrderPresent = false,
                DefaultReferencesL0 = 1
            };
            return new H264Configuration(configuration.LengthSize, new Dictionary<int, H264SequenceParameterSet> { [0] = sequence },
                new Dictionary<int, H264PictureParameterSet> { [0] = picture });
        }

        private static byte[] SyntheticPayload(H264Configuration configuration, int initialization)
        {
            string prefix = H264TestBits.Ue(0) + H264TestBits.Ue(0) + H264TestBits.Ue(0) +
                new string('0', configuration.Sequences[0].FrameNumberBits) + "000";
            return H264TestBits.Rbsp(prefix + H264TestBits.Ue(initialization) + H264TestBits.Ue(3) + H264TestBits.Ue(1) + "10101010");
        }
    }
}
