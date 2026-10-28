using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal static class H264CabacPictureDecoder
    {
        public static H264Picture Decode(ref H264BitReader bits, H264SliceHeader header,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? reason = H264Support.GetUnsupportedReason(header);
            if (!header.Picture.Cabac || reason is not null)
            {
                throw new NotSupportedException(reason ?? "CABAC reconstruction requires a CABAC slice.");
            }
            H264Picture result = new(header.Sequence, allocator);
            bool complete = false;
            try
            {
                using H264CabacIntraState state = new(header.Sequence, allocator);
                H264CabacIntraReader reader = new(ref bits, header);
                H264CabacIntraSyntax syntax = new(reader, result, state);
                int qp = header.Qp;
                for (int address = 0; address < result.Quantizers.Length; address++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int type = syntax.ReadType(ref bits, address);
                    if (type == 25)
                    {
                        syntax.ReadPcm(ref bits, header, address);
                    }
                    else
                    {
                        syntax.ReadPrediction(ref bits, header, address, type);
                        int pattern = syntax.ReadPattern(ref bits, address, type);
                        qp = syntax.ReadQuantizer(ref bits, type, pattern, qp);
                        result.Quantizers[address] = (byte)qp;
                        int x = address % header.Sequence.WidthInMbs * 16;
                        int y = address / header.Sequence.WidthInMbs * 16;
                        H264IntraReconstruction.Read(ref bits, syntax, result, header, x, y, type, state.ChromaModes[address], pattern, qp);
                    }
                    bool ended = reader.Termination(ref bits);
                    if (ended != (address == result.Quantizers.Length - 1))
                    {
                        throw new InvalidDataException("The H.264 CABAC slice does not end at the complete picture boundary.");
                    }
                }
                bits.ReadCabacPadding();
                H264Deblocking.Apply(result, header, cancellationToken, []);
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
