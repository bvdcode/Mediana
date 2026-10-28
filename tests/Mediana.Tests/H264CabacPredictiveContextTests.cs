using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264CabacPredictiveContextTests
    {
        [Fact]
        public async Task EveryPredictiveContextInitializationAndQuantizerMatchesTheNormativeReference() =>
            await H264CabacTestInput.VerifyPredictiveContextStatesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"));

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(10)]
        [InlineData(24)]
        [InlineData(39)]
        [InlineData(276)]
        [InlineData(398)]
        [InlineData(402)]
        public void RejectsContextsWithoutAPredictiveInitialization(int contextIndex) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => H264CabacPredictiveContexts.GetIndex(contextIndex));

        [Theory]
        [InlineData(-1)]
        [InlineData(3)]
        public void RejectsInvalidInitializationSelection(int initialization) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => H264CabacPredictiveContexts.Create(26, initialization));

        [Theory]
        [InlineData(0, -6, 0)]
        [InlineData(1, -6, 0)]
        [InlineData(2, -6, 0)]
        [InlineData(0, 63, 51)]
        [InlineData(1, 63, 51)]
        [InlineData(2, 63, 51)]
        public void ClipsTheInitializationQuantizer(int initialization, int qp, int clipped)
        {
            H264CabacContext[] expected = H264CabacPredictiveContexts.Create(clipped, initialization);
            H264CabacContext[] actual = H264CabacPredictiveContexts.Create(qp, initialization);
            Assert.Equal(expected.Select(context => (context.State, context.MostProbableSymbol)),
                actual.Select(context => (context.State, context.MostProbableSymbol)));
        }
    }
}
