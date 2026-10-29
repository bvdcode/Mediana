using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    internal static class H264BidirectionalTestInput
    {
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
