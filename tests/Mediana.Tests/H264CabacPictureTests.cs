using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264CabacPictureTests
    {
        [Fact]
        public async Task RealHighProfileIdrMatchesEveryIndependentFilteredYuvSample()
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-aac.mp4"), 0);
            byte[] expected = await Mp4TestData.ReadAsync("avc-aac.frame0.yuv");
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264Picture picture = Decode(nal, configuration, allocator))
            {
                H264TestInput.VerifyPicture(picture, expected);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task MixedIntra4And16MacroblocksMatchAllIndependentReferencePictures() =>
            await H264CabacTestInput.VerifyIntraPicturesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"));

        [Fact]
        public async Task TruncationAndExtraDataReleasePictureAndWorkerStorage()
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-aac.mp4"), 0);
            MediaBufferAllocator allocator = new(1024 * 1024);
            foreach (int length in new[] { 6, 8, 16, 64, nal.Length / 2, nal.Length - 1 })
            {
                byte[] truncated = nal[..length];
                Assert.Throws<InvalidDataException>(() => Decode(truncated, configuration, allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            foreach (byte[] extra in new byte[][] { [0], [0, 1], [0, 0, 3, 4] })
            {
                byte[] extended = [.. nal, .. extra];
                Assert.Throws<InvalidDataException>(() => Decode(extended, configuration, allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        [Theory]
        [InlineData(new byte[] { 0, 0, 3 })]
        [InlineData(new byte[] { 0, 0, 3, 0, 0, 3 })]
        public async Task AcceptsEscapedZeroWordsWithoutChangingPixels(byte[] padding)
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-aac.mp4"), 0);
            byte[] extended = [.. nal, .. padding];
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264Picture picture = Decode(extended, configuration, allocator))
            {
                H264TestInput.VerifyPicture(picture, await Mp4TestData.ReadAsync("avc-aac.frame0.yuv"));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task CancellationAndBothAllocationFailuresLeaveNoStorage()
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-aac.mp4"), 0);
            H264SequenceParameterSet sequence = configuration.Sequences[0];
            MediaBufferAllocator measurement = new(1024 * 1024);
            long pictureBytes;
            using (H264Picture picture = new(sequence, measurement))
            {
                pictureBytes = measurement.AllocatedBytes;
            }
            long workerBytes = sequence.WidthInMbs * sequence.HeightInMapUnits * 4;
            foreach (long budget in new[] { 1, pictureBytes, pictureBytes + workerBytes - 1 })
            {
                MediaBufferAllocator allocator = new(budget);
                Assert.Throws<MediaMemoryLimitException>(() => Decode(nal, configuration, allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            MediaBufferAllocator cancelled = new(1024 * 1024);
            Assert.Throws<OperationCanceledException>(() => Decode(nal, configuration, cancelled, new CancellationToken(true)));
            Assert.Equal(0, cancelled.AllocatedBytes);
        }

        [Fact]
        public async Task PcmRestartsTheArithmeticEngineAndReturnsExactSamples()
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-aac.mp4"), 0);
            H264BitReader original = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref original, configuration, nal[0]);
            header = header with
            {
                Sequence = header.Sequence with { WidthInMbs = 1, HeightInMapUnits = 1, Width = 16, Height = 16 },
                Qp = 26,
                DeblockingIdc = 1
            };
            byte[] samples = Enumerable.Range(0, 384).Select(index => (byte)(index % 253 + 1)).ToArray();
            byte[] payload = [0xfe, 0xf8, .. samples, 0xfe, 0x80];
            H264BitReader bits = new(payload);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264Picture picture = H264CabacPictureDecoder.Decode(ref bits, header, allocator))
            {
                H264TestInput.VerifyPicture(picture, samples);
                Assert.Equal(0, picture.Quantizers[0]);
                Assert.All(picture.Counts(0).ToArray(), count => Assert.Equal(16, count));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task AnActualIntra8FlagFailsAndReleasesPictureAndWorkerStorage()
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-aac.mp4"), 0);
            H264BitReader original = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref original, configuration, nal[0]);
            Assert.True(header.Picture.Transform8x8);
            byte[] initial = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264", "cabac-intra-contexts.bin"));
            H264CabacIntervalWriter writer = new(header.Qp);
            writer.Initialize(3, initial[header.Qp * 227]);
            writer.Initialize(399, initial[header.Qp * 227 + 224]);
            writer.Decision(false, 3, 0, 0);
            writer.Decision(true, 399, 0, 0);
            writer.Termination(true);
            byte[] payload = writer.ToEbsp();
            MediaBufferAllocator allocator = new(1024 * 1024);
            NotSupportedException error = Assert.Throws<NotSupportedException>(() => DecodePayload(payload, header, allocator));
            Assert.Contains("8x8", error.Message);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static H264Picture DecodePayload(byte[] payload, H264SliceHeader header, MediaBufferAllocator allocator)
        {
            H264BitReader bits = new(payload);
            return H264PictureDecoder.Decode(ref bits, header, [], allocator);
        }

        private static H264Picture Decode(byte[] nal, H264Configuration configuration, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
            return H264CabacPictureDecoder.Decode(ref bits, header, allocator, cancellationToken);
        }
    }
}
