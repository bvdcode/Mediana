using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264BiPredictionWeightTests
    {
        [Fact]
        public void AverageExplicitPlanesAndImplicitSampleVectorsMatch() => H264BiPredictionTestInput.Verify();

        [Theory]
        [InlineData(0, 6, 2, 0, 43, 21)]
        [InlineData(6, 0, 4, 0, 43, 21)]
        [InlineData(0, 0, 1, 0, 32, 32)]
        [InlineData(0, 1, -1, 0, 128, -64)]
        [InlineData(0, 1, -2, 0, 32, 32)]
        [InlineData(0, 1, 2, 0, -64, 128)]
        [InlineData(0, 1, 3, 0, 32, 32)]
        [InlineData(0, 6, 2, 1, 32, 32)]
        [InlineData(0, 6, 2, 2, 32, 32)]
        [InlineData(int.MinValue, int.MaxValue, 0, 0, 0, 64)]
        [InlineData(int.MaxValue, int.MinValue, 0, 0, 0, 64)]
        public void ImplicitPocScalingClipsDistancesAndHandlesLongTermReferences(int poc0, int poc1, int current,
            int longTerm, int expected0, int expected1)
        {
            MediaBufferAllocator allocator = new(2048);
            H264SequenceParameterSet sequence = new() { WidthInMbs = 1, HeightInMapUnits = 1, FrameMbsOnly = true };
            using H264Picture picture0 = new(sequence, allocator, H264SliceKind.P);
            using H264Picture picture1 = new(sequence, allocator, H264SliceKind.P);
            H264ReferencePicture l0 = new(picture0, 0, new(poc0, poc0), longTerm == 1 ? 0 : null);
            H264ReferencePicture l1 = new(picture1, 1, new(poc1, poc1), longTerm == 2 ? 0 : null);
            H264BiPredictionWeights weights = H264BiPredictionWeights.Implicit(new(current, current), l0, l1);
            Assert.Equal(expected0, weights.Weight0);
            Assert.Equal(expected1, weights.Weight1);
            Assert.Equal(5, weights.Denominator);
            Assert.Equal(0, weights.Offset);
        }

        [Theory]
        [InlineData(0, 64, 64)]
        [InlineData(6, -128, 0)]
        [InlineData(7, 64, 63)]
        public void ExplicitWeightSumIncludesBothNormativeLimits(int denominator, int w0, int w1)
        {
            H264WeightTable l0 = Table(denominator, w0);
            H264WeightTable l1 = Table(denominator, w1);
            H264BiPredictionWeights weights = H264BiPredictionWeights.Explicit(l0, 0, l1, 0, 0);
            Assert.Equal(w0, weights.Weight0);
            Assert.Equal(w1, weights.Weight1);
        }

        [Theory]
        [InlineData(0, 65, 64)]
        [InlineData(6, -128, -1)]
        [InlineData(7, 64, 64)]
        [InlineData(7, 128, 128)]
        public void ExplicitWeightSumRejectsInvalidBiprediction(int denominator, int w0, int w1)
        {
            Assert.Throws<InvalidDataException>(() => H264BiPredictionWeights.Explicit(Table(denominator, w0), 0, Table(denominator, w1), 0, 0));
        }

        [Fact]
        public void ExplicitPredictionRequiresMatchingListDenominators()
        {
            Assert.Throws<InvalidDataException>(() => H264BiPredictionWeights.Explicit(Table(1, 1), 0, Table(2, 1), 0, 0));
        }

        [Fact]
        public void FullInPlacePredictionWorksAndPartialOverlapIsRejected()
        {
            byte[] left = [0, 1, 255];
            byte[] right = [255, 254, 0];
            H264BiPredictionWeights.Average.Apply(left, right, left);
            Assert.Equal(new byte[] { 128, 128, 128 }, left);
            byte[] overlap = [0, 1, 2, 3];
            Assert.Throws<ArgumentException>(() => H264BiPredictionWeights.Average.Apply(overlap.AsSpan(0, 3), right, overlap.AsSpan(1, 3)));
            Assert.Equal(new byte[] { 0, 1, 2, 3 }, overlap);
            Assert.Throws<ArgumentException>(() => H264BiPredictionWeights.Average.Apply(left, right, new byte[2]));
        }

        private static H264WeightTable Table(int denominator, int weight) => new(denominator, denominator, [new(weight, 0, weight, 0, weight, 0)]);
    }
}
