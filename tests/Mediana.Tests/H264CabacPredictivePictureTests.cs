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

        [Fact]
        public async Task ActualInter8x8TransformUseFailsAndReleasesAllWorkers()
        {
            H264SliceHeader header = await H264CabacPredictionTestInput.ReadHeaderAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"));
            header = header with
            {
                Sequence = header.Sequence with { WidthInMbs = 1, HeightInMapUnits = 1, Width = 16, Height = 16 },
                Picture = header.Picture with { Transform8x8 = true },
                ActiveReferencesL0 = 1,
                Weights = null,
                Qp = 26,
                DeblockingIdc = 1
            };
            byte[] states = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264", "cabac-predictive-contexts.bin"));
            H264CabacIntervalWriter writer = H264CabacPredictionTestInput.CreateWriter(states, header);
            foreach (int context in new[] { 11, 14, 15, 16, 40, 47 })
            {
                writer.Decision(false, context, 0, 0);
            }
            writer.Decision(true, 73, 0, 0);
            writer.Decision(false, 73, 0, 0);
            writer.Decision(false, 73, 0, 0);
            writer.Decision(false, 76, 0, 0);
            writer.Decision(false, 77, 0, 0);
            writer.Decision(true, 399, 0, 0);
            writer.Termination(true);
            byte[] payload = writer.ToEbsp();
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264Picture reference = new(header.Sequence, allocator))
            {
                long retained = allocator.AllocatedBytes;
                H264ReferencePicture[] references = [new(reference, 0)];
                NotSupportedException error = Assert.Throws<NotSupportedException>(() => DecodePayload(payload, header, references, allocator));
                Assert.Contains("inter 8x8", error.Message);
                Assert.Equal(retained, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static H264Picture DecodePayload(byte[] payload, H264SliceHeader header,
            IReadOnlyList<H264ReferencePicture?> references, MediaBufferAllocator allocator)
        {
            H264BitReader bits = new(payload);
            return H264CabacPictureDecoder.Decode(ref bits, header, references, allocator);
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
