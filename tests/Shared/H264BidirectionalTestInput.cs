using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264BidirectionalTestInput
    {
        public static async Task VerifyReferenceListsAsync(string root)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            H264PictureOrderState order = new();
            int[] expectedOrder = [0, 6, 2, 4, 8];
            using (H264ReferenceBuffer references = new())
            {
                for (int packet = 0; packet < expectedOrder.Length; packet++)
                {
                    (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Path.Combine(root, "avc-segments.mp4"), packet);
                    H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
                    H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
                    order = order.Advance(header);
                    if (order.Current != new H264PictureOrder(expectedOrder[packet], expectedOrder[packet]))
                    {
                        throw new InvalidDataException("The B sequence picture order differs from the independent trace.");
                    }
                    if (header.Kind == H264SliceKind.B)
                    {
                        H264BidirectionalReferences lists = references.GetBidirectionalLists(header, order.Current);
                        int[] expectedL0 = packet == 2 ? [0] : [2, 0];
                        if (!lists.L0.Select(value => value?.FrameNumber).SequenceEqual(expectedL0.Select(value => (int?)value))
                            || lists.L1.Count != 1 || lists.L1[0]?.FrameNumber != 1)
                        {
                            throw new InvalidDataException("The B reference lists differ from the expected decoded reference picture order.");
                        }
                    }
                    H264Picture picture = new(header.Sequence, allocator);
                    bool stored = false;
                    try
                    {
                        references.Store(picture, header, order.Current);
                        stored = true;
                    }
                    finally
                    {
                        if (!stored)
                        {
                            picture.Dispose();
                        }
                    }
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("B reference list verification retained picture storage.");
            }
        }

        public static async Task VerifyHeadersAsync(string root)
        {
            for (int packet = 2; packet <= 3; packet++)
            {
                (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Path.Combine(root, "avc-segments.mp4"), packet);
                H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
                H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
                int expectedReferenceIndicator = packet == 2 ? 2 : 0;
                int expectedQp = packet == 2 ? 26 : 28;
                if (header.Kind != H264SliceKind.B || header.IsIdr || header.FirstMacroblock != 0
                    || header.FrameNumber != packet || header.PictureOrderLsb != (packet - 1) * 2
                    || header.ActiveReferencesL0 != packet - 1 || header.ActiveReferencesL1 != 1
                    || !header.DirectSpatialMotionPrediction || header.ReferenceIndicator != expectedReferenceIndicator
                    || header.Qp != expectedQp || header.CabacInitIdc != 0 || header.DeblockingIdc != 0
                    || header.AlphaOffset != 0 || header.BetaOffset != 0 || header.AdaptiveMarking
                    || header.ReferenceModifications.Count != 0 || header.ReferenceModificationsL1.Count != 0
                    || header.MemoryOperations.Count != 0 || header.Weights is not null || header.WeightsL1 is not null)
                {
                    throw new InvalidDataException($"The B slice header at packet {packet} differs from the independent trace.");
                }
                bits.AlignOne();
                if (!bits.MoreRbspData())
                {
                    throw new InvalidDataException("The B slice has no CABAC payload after its header.");
                }
            }
        }
    }
}
