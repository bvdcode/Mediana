namespace Mediana.Tests
{
    public class Mp3RealFileTests
    {
        [Fact]
        public async Task EveryRealMp3PacketTagAndPictureMatchesIndependentReferences() =>
            await Mp3ReferenceTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp3"));
    }
}
