using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264WeightTests
    {
        [Fact]
        public void ReadsDefaultWeightsAndExplicitSignedWeights()
        {
            // Denominators 0/0, identity reference, then luma -1/+2 and Cb +2/-1, Cr 0/+1.
            H264BitReader bits = new(H264TestBits.Rbsp("1 1 00 1 011 00100 1 00100 011 1 010"));
            (H264WeightTable table, _) = H264WeightTable.Read(ref bits, 2, 0);
            bits.ReadTrailingBits();
            Assert.Equal(new H264PredictionWeight(1, 0, 1, 0, 1, 0), table.Entries[0]);
            Assert.Equal(new H264PredictionWeight(-1, 2, 2, -1, 0, 1), table.Entries[1]);
            byte[] samples = [0, 1, 2, 255];
            table.Apply(1, 0, samples);
            Assert.Equal(new byte[] { 2, 1, 0, 0 }, samples);
            samples = [0, 1, 128, 255];
            table.Apply(1, 1, samples);
            Assert.Equal(new byte[] { 0, 1, 255, 255 }, samples);
            table.Apply(1, 2, samples);
            Assert.All(samples, value => Assert.Equal(1, value));
        }

        [Fact]
        public void ListOneReadsExplicitSignedWeightsWithTheSameDenominators()
        {
            H264BitReader bits = new(H264TestBits.Rbsp("010 011 00 1 011 00100 1 00100 011 1 010"));
            (H264WeightTable l0, H264WeightTable? l1) = H264WeightTable.Read(ref bits, 1, 1);
            bits.ReadTrailingBits();
            Assert.Equal(new[] { new H264PredictionWeight(2, 0, 4, 0, 4, 0) }, l0.Entries);
            Assert.NotNull(l1);
            Assert.Equal(1, l1.LumaDenominator);
            Assert.Equal(2, l1.ChromaDenominator);
            Assert.Equal(new[] { new H264PredictionWeight(-1, 2, 2, -1, 0, 1) }, l1.Entries);
        }

        [Fact]
        public void TruncatedListOneWeightIsRejected()
        {
            byte[] payload = H264TestBits.Rbsp("1 1 00 1" + new string('0', 40), trailing: false);
            Assert.Throws<InvalidDataException>(() => ReadWeights(payload));
        }

        private static void ReadWeights(byte[] payload)
        {
            H264BitReader bits = new(payload);
            H264WeightTable.Read(ref bits, 1, 1);
        }

        [Fact]
        public void WeightsRoundBeforeOffsetAndClipAfterwards()
        {
            H264WeightTable table = new(1, 1, [new H264PredictionWeight(1, -1, -1, 128, 127, 127)]);
            byte[] samples = [0, 1, 2, 3, 254, 255];
            table.Apply(0, 0, samples);
            Assert.Equal(new byte[] { 0, 0, 0, 1, 126, 127 }, samples);
            samples = [0, 1, 2, 3, 254, 255];
            table.Apply(0, 1, samples);
            Assert.Equal(new byte[] { 128, 128, 127, 127, 1, 1 }, samples);
        }

        [Fact]
        public void DefaultDenominatorSevenStillPreservesEightBitSamples()
        {
            H264BitReader bits = new(H264TestBits.Rbsp("0001000 0001000 00"));
            (H264WeightTable table, _) = H264WeightTable.Read(ref bits, 1, 0);
            byte[] samples = [0, 1, 127, 128, 255];
            table.Apply(0, 0, samples);
            Assert.Equal(new byte[] { 0, 1, 127, 128, 255 }, samples);
        }
    }
}
