using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264CabacIntraContextTests
    {
        [Fact]
        public async Task EveryIntraContextAndQuantizerMatchesTheNormativeReference() =>
            await H264CabacTestInput.VerifyIntraContextStatesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"));

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(2)]
        [InlineData(11)]
        [InlineData(59)]
        [InlineData(276)]
        [InlineData(398)]
        [InlineData(402)]
        public void RejectsContextsWithoutAnIntraInitialization(int contextIndex) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => H264CabacIntraContexts.GetIndex(contextIndex));

        [Fact]
        public async Task ReadsTheRealIntraSliceThroughAlignedInitializedContexts() =>
            await H264CabacTestInput.VerifyRealIntraPrefixAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"));

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        public async Task RejectsOtherEntropyModesAndUnsupportedSlicesBeforeReading(bool cabac, bool unsupported)
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-tail.mov"), 0);
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
            header = header with { Picture = header.Picture with { Cabac = cabac } };
            if (unsupported)
            {
                header = header with { Kind = H264SliceKind.B };
            }
            Assert.Throws<NotSupportedException>(() => Initialize(header));
        }

        private static void Initialize(H264SliceHeader header)
        {
            H264BitReader bits = new([]);
            _ = new H264CabacReader(ref bits, header);
        }
    }
}
