namespace Mediana.Tests
{
    public class WavePcmTests
    {
        [Theory]
        [InlineData("u8-mono")]
        [InlineData("s16-stereo")]
        [InlineData("s24-stereo")]
        [InlineData("s32-stereo")]
        [InlineData("f32-stereo")]
        [InlineData("f64-stereo")]
        [InlineData("s24-surround")]
        public Task EverySampleAndTimestampMatchesIndependentPcm(string name) =>
            WavePcmTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Wave"), name);
    }
}
