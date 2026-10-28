namespace Mediana.Tests
{
    public class H264CabacTransformTests
    {
        [Fact]
        public async Task All8x8ContextInitializationsMatchTheIndependentNormativeReference() =>
            await H264CabacTransformTestInput.VerifyContextsAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"));

        [Fact]
        public async Task Every8x8ResidualPositionAndLevelBoundaryMatchesIndependentIntervals() =>
            await H264CabacTransformTestInput.VerifyResidualsAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"),
                Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"));
    }
}
