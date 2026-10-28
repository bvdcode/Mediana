using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal static class H264PictureDecoder
    {
        public static H264Picture Decode(ref H264BitReader bits, H264SliceHeader header,
            IReadOnlyList<H264ReferencePicture?> references, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? reason = H264Support.GetUnsupportedReason(header);
            if (reason is not null)
            {
                throw new NotSupportedException(reason);
            }
            if (header.Picture.Cabac)
            {
                return H264CabacPictureDecoder.Decode(ref bits, header, allocator, cancellationToken);
            }
            H264Picture result = new(header.Sequence, allocator);
            bool complete = false;
            try
            {
                int qp = header.Qp;
                int address = 0;
                while (address < result.Quantizers.Length && bits.MoreRbspData())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (header.Kind == H264SliceKind.P)
                    {
                        int skip = bits.ReadUe(result.Quantizers.Length - address);
                        for (int index = 0; index < skip; index++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            H264InterMacroblock.Read(ref bits, result, header, references, address++, ref qp, skip: true);
                        }
                        if (address == result.Quantizers.Length || (skip > 0 && !bits.MoreRbspData()))
                        {
                            break;
                        }
                    }
                    switch (header.Kind)
                    {
                        case H264SliceKind.I:
                            H264IntraMacroblock.Read(ref bits, result, header, address, bits.ReadUe(25), ref qp);
                            break;
                        case H264SliceKind.P:
                            H264InterMacroblock.Read(ref bits, result, header, references, address, ref qp);
                            break;
                        default:
                            throw new NotSupportedException("Only H.264 CAVLC I and P picture reconstruction is implemented.");
                    }
                    address++;
                }
                if (address != result.Quantizers.Length)
                {
                    throw new NotSupportedException("The H.264 slice does not cover the complete picture.");
                }
                bits.ReadTrailingBits();
                H264Deblocking.Apply(result, header, cancellationToken, references);
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
    }
}
