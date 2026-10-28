namespace Mediana.Tests
{
    public class PreviewIntegrationTests
    {
        [Fact]
        public async Task RotatedMp4ProducesAnOrientedPngMatchingIndependentColorAndGeometry()
        {
            await RotatedPreviewTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"));
        }
    }
}
