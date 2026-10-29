namespace Mediana.Tests
{
    public class FlacRealFileTests
    {
        [Theory]
        [InlineData(127)]
        [InlineData(16384)]
        public async Task RealMetadataPicturesAndCompressedBytesMatchIndependentReferences(int packetBytes)
        {
            await FlacMetadataTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Flac"), packetBytes);
        }
    }
}
