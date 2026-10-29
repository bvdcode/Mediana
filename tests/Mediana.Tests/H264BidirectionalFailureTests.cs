using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264BidirectionalFailureTests
    {
        [Theory]
        [InlineData("avc-cavlc-b-spatial.mp4")]
        [InlineData("avc-cavlc-b-temporal.mp4")]
        public async Task DamagedOrTruncatedBPayloadAndCancellationReleaseOnlyTheNewPicture(string file)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264ReferenceBuffer references = new())
            {
                H264PictureOrderState order = new();
                for (int index = 0; index < 2; index++)
                {
                    (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor(file), index);
                    H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
                    H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
                    order = order.Advance(header);
                    references.Store(H264PictureDecoder.Decode(ref bits, header, references.GetList0(header), allocator), header, order.Current);
                }
                (H264Configuration bConfiguration, byte[] bNal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor(file), 2);
                long before = allocator.AllocatedBytes;
                Random random = new(2642);
                for (int index = 0; index < 64; index++)
                {
                    byte[] damaged = (byte[])bNal.Clone();
                    damaged[random.Next(damaged.Length)] ^= (byte)(1 << random.Next(8));
                    Exception? error = Record.Exception(() =>
                    {
                        using H264Picture picture = Decode(damaged, bConfiguration, references, order, allocator);
                    });
                    Assert.True(error is null or InvalidDataException or NotSupportedException, error?.ToString());
                    Assert.Equal(before, allocator.AllocatedBytes);
                    Assert.Equal(2, references.Count);
                }
                foreach (int length in new[] { 0, 1, 2, 3, bNal.Length / 2, bNal.Length - 1 })
                {
                    Exception? error = Record.Exception(() =>
                    {
                        using H264Picture picture = Decode(bNal.AsSpan(0, length), bConfiguration, references, order, allocator);
                    });
                    Assert.True(error is InvalidDataException or NotSupportedException, error?.ToString());
                    Assert.Equal(before, allocator.AllocatedBytes);
                }
                Assert.Throws<OperationCanceledException>(() => Decode(bNal, bConfiguration, references, order, allocator, new CancellationToken(true)));
                Assert.Equal(before, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static H264Picture Decode(ReadOnlySpan<byte> nal, H264Configuration configuration, H264ReferenceBuffer references,
            H264PictureOrderState order, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
            H264PictureOrder current = order.Advance(header).Current;
            return H264BidirectionalPictureDecoder.Decode(ref bits, header, references.GetBidirectionalLists(header, current), current, allocator, cancellationToken);
        }
    }
}
