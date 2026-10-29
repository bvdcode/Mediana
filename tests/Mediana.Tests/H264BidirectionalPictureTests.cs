namespace Mediana.Tests
{
    public class H264BidirectionalPictureTests
    {
        [Theory]
        [InlineData("avc-cavlc-b-spatial")]
        [InlineData("avc-cavlc-b-temporal")]
        [InlineData("avc-aac")]
        [InlineData("avc-segments")]
        [InlineData("avc-cabac-b-0")]
        [InlineData("avc-cabac-b-1")]
        [InlineData("avc-cabac-b-2")]
        public Task AllReorderedPicturesMatchIndependentYuv(string name) =>
            H264BidirectionalPictureTestInput.VerifyPicturesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), name);

        [Theory]
        [InlineData("avc-aac")]
        [InlineData("avc-segments")]
        public Task CabacPublicSessionMatchesIndependentPicturesAndTiming(string name) =>
            H264BidirectionalPictureTestInput.VerifySessionAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), name);

        [Theory]
        [InlineData("avc-cavlc-b-spatial")]
        [InlineData("avc-cavlc-b-temporal")]
        [InlineData("avc-cabac-b-0")]
        [InlineData("avc-cabac-b-1")]
        [InlineData("avc-cabac-b-2")]
        public async Task PublicSessionAndSelectedBPreviewMatchIndependentSamples(string name)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Mp4");
            await H264BidirectionalPictureTestInput.VerifySessionAsync(root, name);
            await H264BidirectionalPictureTestInput.VerifyPreviewAsync(root, name);
        }
    }
}
