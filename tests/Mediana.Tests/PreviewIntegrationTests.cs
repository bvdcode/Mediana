namespace Mediana.Tests
{
    public class PreviewIntegrationTests
    {
        [Fact]
        public async Task RotatedMp4ProducesAnOrientedPngMatchingIndependentColorAndGeometry()
        {
            await RotatedPreviewTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"));
        }

        [Fact]
        public async Task CabacMp4SelectsTheNextPresentationTimeAndProducesAnIndependentSrgbPng()
        {
            await CabacPreviewTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"));
        }
    }
}
