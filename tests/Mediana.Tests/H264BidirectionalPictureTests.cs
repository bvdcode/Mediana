namespace Mediana.Tests
{
    public class H264BidirectionalPictureTests
    {
        [Theory]
        [InlineData("avc-cavlc-b-spatial", true)]
        [InlineData("avc-cavlc-b-temporal", false)]
        public Task AllReorderedPicturesMatchIndependentYuv(string name, bool spatial) =>
            H264BidirectionalPictureTestInput.VerifyPicturesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), name, spatial);

        [Theory]
        [InlineData("avc-cavlc-b-spatial")]
        [InlineData("avc-cavlc-b-temporal")]
        public async Task PublicSessionAndSelectedBPreviewMatchIndependentSamples(string name)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Mp4");
            await H264BidirectionalPictureTestInput.VerifySessionAsync(root, name);
            await H264BidirectionalPictureTestInput.VerifyPreviewAsync(root, name);
        }
    }
}
