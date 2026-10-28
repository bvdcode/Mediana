namespace Mediana.Tests
{
    public class PreviewIntegrationTests
    {
        [Fact]
        public async Task RotatedMp4ProducesAnOrientedPngMatchingIndependentColorAndGeometry()
        {
            await RotatedPreviewTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"));
        }

        [Theory]
        [InlineData("avc-cabac-intra", 3, 5)]
        [InlineData("avc-cabac-p0", 5, 10)]
        [InlineData("avc-cabac-p1", 5, 10)]
        [InlineData("avc-cabac-p2", 5, 10)]
        public async Task CabacMp4SelectsTheNextPresentationTimeAndProducesAnIndependentSrgbPng(string name, int frameIndex, int fps)
        {
            await CabacPreviewTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), name, frameIndex, fps);
        }
    }
}
