using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal static class H264IntraDecoder
    {
        public static H264Picture Decode(ReadOnlySpan<byte> nal, H264Configuration configuration,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            H264BitReader bits = H264Nal.Payload(nal, 5);
            H264IntraSliceHeader header = H264IntraSliceHeader.Read(ref bits, configuration);
            H264Picture result = new(header.Sequence, allocator);
            bool complete = false;
            try
            {
                int qp = header.Qp;
                for (int address = 0; address < result.Quantizers.Length; address++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!bits.MoreRbspData())
                    {
                        throw new NotSupportedException("The H.264 intra slice does not cover the complete picture.");
                    }
                    H264IntraMacroblock.Read(ref bits, result, header, address, ref qp);
                }
                bits.ReadTrailingBits();
                H264IntraDeblocking.Apply(result, header, cancellationToken);
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
