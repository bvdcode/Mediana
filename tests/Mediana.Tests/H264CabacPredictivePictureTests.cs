using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264CabacPredictivePictureTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task CompleteWeightedPredictiveSequenceMatchesAllIndependentYuvSamples(int initialization)
        {
            await H264CabacTestInput.VerifyPredictivePicturesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"),
                $"avc-cabac-p{initialization}", 12, 94, 62, initialization);
        }

        [Fact]
        public async Task MovingMixedPartitionSequenceMatchesAllIndependentYuvSamples() =>
            await H264CabacTestInput.VerifyPredictivePicturesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"),
                "avc-cabac-motion", 18, 190, 126, 2);

        [Fact]
        public async Task EveryWorkerAllocationFailureAndMissingReferencesReleasePictureStorage()
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-cabac-p0.mp4"), 1);
            H264SequenceParameterSet sequence = configuration.Sequences[0];
            MediaBufferAllocator measurement = new(1024 * 1024);
            long pictureBytes;
            using (H264Picture picture = new(sequence, measurement))
            {
                pictureBytes = measurement.AllocatedBytes;
            }
            long stateBytes = sequence.WidthInMbs * sequence.HeightInMapUnits * 4;
            long motionBytes = sequence.CodedWidth * sequence.CodedHeight / 16 * sizeof(short) * 2;
            foreach (long budget in new[] { 1, pictureBytes, pictureBytes + stateBytes, pictureBytes + stateBytes + motionBytes - 1 })
            {
                MediaBufferAllocator allocator = new(budget);
                Assert.Throws<MediaMemoryLimitException>(() => Decode(nal, configuration, [], allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            MediaBufferAllocator exact = new(pictureBytes + stateBytes + motionBytes);
            InvalidDataException error = Assert.Throws<InvalidDataException>(() => Decode(nal, configuration, [], exact));
            Assert.Contains("unavailable reference", error.Message);
            Assert.Equal(0, exact.AllocatedBytes);
        }

        [Fact]
        public async Task DamagedPredictivePicturesReleaseWorkersAndPreservePreviousReferences()
        {
            (H264Configuration configuration, byte[] first) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-cabac-p0.mp4"), 0);
            (_, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-cabac-p0.mp4"), 1);
            byte[] expected = await Mp4TestData.ReadAsync("avc-cabac-p0.sequence.yuv");
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264ReferenceBuffer references = new())
            {
                H264TestInput.VerifyAndStorePicture(first, configuration, references, allocator, expected.AsSpan(0, 94 * 62 * 3 / 2));
                H264BitReader headerBits = H264Nal.Payload(nal, H264Nal.Type(nal));
                H264SliceHeader header = H264SliceHeaderReader.Read(ref headerBits, configuration, nal[0]);
                IReadOnlyList<H264ReferencePicture?> list = references.GetList0(header);
                long retained = allocator.AllocatedBytes;
                foreach (int length in new[] { 6, 16, nal.Length / 2 })
                {
                    byte[] truncated = nal[..length];
                    Assert.Throws<InvalidDataException>(() => Decode(truncated, configuration, list, allocator));
                    Assert.Equal(retained, allocator.AllocatedBytes);
                }
                byte[] extra = [.. nal, 1];
                Assert.Throws<InvalidDataException>(() => Decode(extra, configuration, list, allocator));
                Assert.Equal(retained, allocator.AllocatedBytes);
                H264TestInput.VerifyAndStorePicture(nal, configuration, references, allocator,
                    expected.AsSpan(94 * 62 * 3 / 2, 94 * 62 * 3 / 2));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static H264Picture Decode(byte[] nal, H264Configuration configuration,
            IReadOnlyList<H264ReferencePicture?> references, MediaBufferAllocator allocator)
        {
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
            return H264PictureDecoder.Decode(ref bits, header, references, allocator);
        }
    }
}
