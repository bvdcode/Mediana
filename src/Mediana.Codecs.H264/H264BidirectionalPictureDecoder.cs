using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal static class H264BidirectionalPictureDecoder
    {
        public static H264Picture Decode(ref H264BitReader bits, H264SliceHeader header, H264BidirectionalReferences references,
            H264PictureOrder order, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? reason = H264Support.GetUnsupportedReason(header);
            if (header.Kind != H264SliceKind.B || header.Picture.Cabac || reason is not null)
            {
                throw new NotSupportedException(reason ?? "B reconstruction requires a CAVLC B slice.");
            }
            H264Picture result = new(header.Sequence, allocator, header.Kind);
            bool complete = false;
            try
            {
                int qp = header.Qp;
                int address = 0;
                while (address < result.Quantizers.Length && bits.MoreRbspData())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int skip = bits.ReadUe(result.Quantizers.Length - address);
                    for (int index = 0; index < skip; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ReadMacroblock(ref bits, result, header, references, order, address++, ref qp, true);
                    }
                    if (address == result.Quantizers.Length || skip > 0 && !bits.MoreRbspData())
                    {
                        break;
                    }
                    ReadMacroblock(ref bits, result, header, references, order, address++, ref qp, false);
                }
                if (address != result.Quantizers.Length)
                {
                    throw new NotSupportedException("The H.264 slice does not cover the complete picture.");
                }
                bits.ReadTrailingBits();
                H264Deblocking.Apply(result, header, cancellationToken, references.L0, references.L1);
                result.CaptureReferences(references.L0, references.L1);
                complete = true;
                return result;
            }
            finally
            {
                if (!complete)
                {
                    result.Dispose();
                }
            }
        }

        private static void ReadMacroblock(ref H264BitReader bits, H264Picture target, H264SliceHeader header,
            H264BidirectionalReferences references, H264PictureOrder order, int address, ref int qp, bool skip)
        {
            int type = skip ? 0 : bits.ReadUe(48);
            if (type >= 23)
            {
                H264IntraMacroblock.Read(ref bits, target, header, address, type - 23, ref qp);
                return;
            }
            int x = address % header.Sequence.WidthInMbs * 16;
            int y = address / header.Sequence.WidthInMbs * 16;
            Span<byte> prediction = stackalloc byte[384];
            bool noSmallPartitions = H264BidirectionalSyntax.Read(ref bits, target, header, references, order, x, y, type, prediction);
            H264InterMacroblock.ReadResidual(ref bits, target, header, address, ref qp, prediction, noSmallPartitions, skip);
        }
    }
}
