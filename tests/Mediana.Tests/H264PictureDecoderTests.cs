using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264PictureDecoderTests
    {
        [Fact]
        public async Task BaselineSequenceMatchesEveryIndependentFilteredYuvSample()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264ReferenceBuffer references = new())
            {
                for (int index = 0; index < 6; index++)
                {
                    (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-tail.mov"), index);
                    byte[] expected = await Mp4TestData.ReadAsync($"avc-tail.frame{index}.yuv");
                    H264TestInput.VerifyAndStorePicture(nal, configuration, references, allocator, expected);
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task DamagedPFramesReleaseCurrentStorageAndPreserveReferences()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264ReferenceBuffer references = new())
            {
                (H264Configuration configuration, byte[] idr) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-tail.mov"), 0);
                byte[] expected = await Mp4TestData.ReadAsync("avc-tail.frame0.yuv");
                H264TestInput.VerifyAndStorePicture(idr, configuration, references, allocator, expected);
                (_, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-tail.mov"), 1);
                long before = allocator.AllocatedBytes;
                Random random = new(2641);
                for (int index = 0; index < 128; index++)
                {
                    byte[] damaged = (byte[])nal.Clone();
                    damaged[random.Next(damaged.Length)] ^= (byte)(1 << random.Next(8));
                    Exception? error = Record.Exception(() =>
                    {
                        using H264Picture picture = Decode(damaged, configuration, references, allocator);
                    });
                    Assert.True(error is null or InvalidDataException or NotSupportedException, error?.ToString());
                    Assert.Equal(before, allocator.AllocatedBytes);
                    Assert.Equal(1, references.Count);
                }
                foreach (int length in new[] { 0, 1, 2, 3, nal.Length / 2, nal.Length - 1 })
                {
                    Exception? error = Record.Exception(() =>
                    {
                        using H264Picture picture = Decode(nal.AsSpan(0, length), configuration, references, allocator);
                    });
                    Assert.True(error is InvalidDataException or NotSupportedException, error?.ToString());
                    Assert.Equal(before, allocator.AllocatedBytes);
                }
                Assert.Throws<OperationCanceledException>(() => Decode(nal, configuration, references, allocator, new CancellationToken(true)));
                Assert.Equal(before, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static H264Picture Decode(ReadOnlySpan<byte> nal, H264Configuration configuration,
            H264ReferenceBuffer references, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
            return H264PictureDecoder.Decode(ref bits, header, references.GetList0(header), allocator, cancellationToken);
        }
    }
}
