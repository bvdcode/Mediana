using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264BidirectionalHeaderTests
    {
        private const string Prefix = "1 010 1 0010 0100 ";

        [Fact]
        public Task ReferenceAndNonReferenceHeadersMatchIndependentTrace() =>
            H264BidirectionalTestInput.VerifyHeadersAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"));

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        public void DefaultListsDoNotReadExplicitWeightsForUnweightedOrImplicitPrediction(int weighting)
        {
            H264Configuration configuration = Configuration(weighting);
            H264BitReader bits = new(H264TestBits.Rbsp(Prefix + "0 0 0 0 00101 010"));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, 1);
            bits.ReadTrailingBits();
            Assert.False(header.DirectSpatialMotionPrediction);
            Assert.Equal(2, header.ActiveReferencesL0);
            Assert.Equal(1, header.ActiveReferencesL1);
            Assert.Equal(24, header.Qp);
            Assert.Equal(1, header.DeblockingIdc);
            Assert.Null(header.Weights);
            Assert.Null(header.WeightsL1);
            Assert.Null(H264Support.GetUnsupportedReason(header));
        }

        [Fact]
        public void SeparateListModificationsPrecedeSharedWeightDenominatorsAndCabacInitialization()
        {
            H264Configuration configuration = Configuration(1, cabac: true);
            string modifications = "1 1 1 010 010 00100 1 011 00100 00100 ";
            string weights = "010 011 00 1 011 00100 1 00100 011 1 010 00 ";
            H264BitReader bits = new(H264TestBits.Rbsp(Prefix + "1 0 " + modifications + weights + "011 00101 010"));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, 1);
            bits.ReadTrailingBits();
            Assert.True(header.DirectSpatialMotionPrediction);
            Assert.Equal(new[] { new H264ReferenceModification(0, 0), new H264ReferenceModification(1, 1) }, header.ReferenceModifications);
            Assert.Equal(new[] { new H264ReferenceModification(2, 3) }, header.ReferenceModificationsL1);
            Assert.Equal(2, header.CabacInitIdc);
            Assert.Equal(24, header.Qp);
            H264WeightTable l0 = Assert.IsType<H264WeightTable>(header.Weights);
            H264WeightTable l1 = Assert.IsType<H264WeightTable>(header.WeightsL1);
            Assert.Equal(1, l0.LumaDenominator);
            Assert.Equal(2, l0.ChromaDenominator);
            Assert.Equal(l0.LumaDenominator, l1.LumaDenominator);
            Assert.Equal(l0.ChromaDenominator, l1.ChromaDenominator);
            Assert.Equal(new H264PredictionWeight(2, 0, 4, 0, 4, 0), l0.Entries[0]);
            Assert.Equal(new H264PredictionWeight(-1, 2, 2, -1, 0, 1), l0.Entries[1]);
            Assert.Equal(new[] { new H264PredictionWeight(2, 0, 4, 0, 4, 0) }, l1.Entries);
        }

        [Theory]
        [InlineData(1, 32)]
        [InlineData(32, 1)]
        [InlineData(32, 32)]
        public void OverrideReadsBothReferenceCounts(int l0, int l1)
        {
            string counts = H264TestBits.Ue(l0 - 1) + H264TestBits.Ue(l1 - 1);
            H264BitReader bits = new(H264TestBits.Rbsp(Prefix + "1 1 " + counts + " 0 0 1 010"));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, Configuration(0), 1);
            bits.ReadTrailingBits();
            Assert.Equal(l0, header.ActiveReferencesL0);
            Assert.Equal(l1, header.ActiveReferencesL1);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void OverrideRejectsAnOutOfRangeCountInEitherList(bool list0)
        {
            string counts = list0 ? H264TestBits.Ue(32) + "1" : "1" + H264TestBits.Ue(32);
            byte[] payload = H264TestBits.Rbsp(Prefix + "1 1 " + counts);
            Assert.Throws<InvalidDataException>(() => Read(payload));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public async Task TruncatedBHeadersAreRejected(int packet)
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-segments.mp4"), packet);
            for (int length = 1; length <= 5; length++)
            {
                byte[] truncated = nal[..length];
                Assert.Throws<InvalidDataException>(() => ReadNal(truncated, configuration));
            }
        }

        private static void Read(byte[] payload)
        {
            H264BitReader bits = new(payload);
            H264SliceHeaderReader.Read(ref bits, Configuration(0), 1);
        }

        private static void ReadNal(byte[] nal, H264Configuration configuration)
        {
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
        }

        private static H264Configuration Configuration(int weighting, bool cabac = false)
        {
            H264SequenceParameterSet sequence = new()
            {
                FrameNumberBits = 4,
                PictureOrderBits = 4,
                WidthInMbs = 1,
                HeightInMapUnits = 1,
                FrameMbsOnly = true
            };
            H264PictureParameterSet picture = new(0, 0, cabac, false, 26, 0, 0, true, false, false, false, 2, 1, true, weighting);
            return new H264Configuration(4, new Dictionary<int, H264SequenceParameterSet> { [0] = sequence },
                new Dictionary<int, H264PictureParameterSet> { [0] = picture });
        }
    }
}
