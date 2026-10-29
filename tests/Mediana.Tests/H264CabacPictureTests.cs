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
            using (H264Picture picture = new(sequence, measurement, H264SliceKind.P))
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
            using (H264Picture picture = H264CabacPictureDecoder.Decode(ref bits, header, new([], []), default, allocator))
            {
                H264TestInput.VerifyPicture(picture, samples);
                Assert.Equal(0, picture.Quantizers[0]);
                Assert.All(picture.Counts(0).ToArray(), count => Assert.Equal(16, count));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ExplicitIntra8DcGridUsesAllTransformNeighbourContextsAndReleasesStorage()
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-aac.mp4"), 0);
            H264BitReader original = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref original, configuration, nal[0]);
            Assert.True(header.Picture.Transform8x8);
            header = header with
            {
                Sequence = header.Sequence with { WidthInMbs = 2, HeightInMapUnits = 2, Width = 32, Height = 32 },
                DeblockingIdc = 1
            };
            byte[] initial = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264", "cabac-intra-contexts.bin"));
            H264CabacIntervalWriter writer = new(header.Qp);
            int[] indices = Enumerable.Range(3, 8).Concat(Enumerable.Range(60, 216)).Concat(Enumerable.Range(399, 3)).ToArray();
            for (int index = 0; index < indices.Length; index++)
            {
                writer.Initialize(indices[index], initial[header.Qp * 227 + index]);
            }
            int[] transformContexts = [399, 400, 400, 401];
            int[][] patterns = [[73, 74, 75, 76], [74, 74, 76, 76], [75, 76, 75, 76], [76, 76, 76, 76]];
            for (int address = 0; address < 4; address++)
            {
                writer.Decision(false, 3, 0, 0);
                writer.Decision(true, transformContexts[address], 0, 0);
                for (int block = 0; block < 4; block++)
                {
                    writer.Decision(true, 68, 0, 0);
                }
                writer.Decision(false, 64, 0, 0);
                foreach (int context in patterns[address])
                {
                    writer.Decision(false, context, 0, 0);
                }
                writer.Decision(false, 77, 0, 0);
                writer.Termination(address == 3);
            }
            byte[] payload = writer.ToEbsp();
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264Picture picture = DecodePayload(payload, header, allocator))
            {
                H264TestInput.VerifyPicture(picture, Enumerable.Repeat((byte)128, 1536).ToArray());
                Assert.All(picture.Transforms8x8.ToArray(), flag => Assert.Equal(1, flag));
                Assert.All(picture.Counts(0).ToArray(), count => Assert.Equal(0, count));
            }
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
            return H264CabacPictureDecoder.Decode(ref bits, header, new([], []), default, allocator, cancellationToken);
        }
    }
}
