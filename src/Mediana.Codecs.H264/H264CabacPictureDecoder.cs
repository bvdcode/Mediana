using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal static class H264CabacPictureDecoder
    {
        public static H264Picture Decode(ref H264BitReader bits, H264SliceHeader header,
            IReadOnlyList<H264ReferencePicture?> references, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
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
                using H264CabacMacroblockState state = new(header.Sequence, allocator);
                H264CabacReader reader = new(ref bits, header);
                using H264CabacPredictionReader? predictionReader = header.Kind switch
                {
                    H264SliceKind.I => null,
                    H264SliceKind.P => new H264CabacPredictionReader(reader, result, allocator),
                    _ => throw new NotSupportedException("CABAC reconstruction supports I and P slices.")
                };
                H264CabacPictureSyntax syntax = new(reader, result, state);
                int qp = header.Qp;
                Span<byte> prediction = stackalloc byte[384];
                for (int address = 0; address < result.Quantizers.Length; address++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bool skipped = header.Kind == H264SliceKind.P && H264CabacMacroblockSyntax.ReadSkip(ref bits, reader,
                        state.SkipCondition(state.Left(address)) + state.SkipCondition(state.Top(address)));
                    int type = header.Kind switch
                    {
                        H264SliceKind.I => syntax.ReadType(ref bits, address),
                        H264SliceKind.P => skipped ? 0 : H264CabacMacroblockSyntax.ReadPredictiveType(ref bits, reader),
                        _ => throw new NotSupportedException("CABAC reconstruction supports I and P slices.")
                    };
                    int x = address % header.Sequence.WidthInMbs * 16;
                    int y = address / header.Sequence.WidthInMbs * 16;
                    if (header.Kind == H264SliceKind.P && type < 5)
                    {
                        state.Types[address] = skipped ? H264CabacMacroblockState.Skipped : H264CabacMacroblockState.Inter;
                        bool noSmallPartitions = true;
                        if (skipped)
                        {
                            H264InterPrediction.Skip(result, header, references, x, y, prediction);
                        }
                        else
                        {
                            noSmallPartitions = H264InterPrediction.Read(ref bits,
                                predictionReader ?? throw new InvalidOperationException("The CABAC P reader is missing."),
                                result, header, references, x, y, type, prediction);
                        }
                        int pattern = skipped ? 0 : syntax.ReadPattern(ref bits, address, 0);
                        if (!skipped && (pattern & 15) != 0 && header.Picture.Transform8x8 && noSmallPartitions)
                        {
                            syntax.ReadTransform(ref bits, address);
                        }
                        qp = syntax.ReadQuantizer(ref bits, 0, pattern, qp);
                        result.Quantizers[address] = (byte)qp;
                        H264Residuals.ReadInterLuma(ref bits, syntax, result, x, y, pattern, qp, prediction[..256]);
                        H264Residuals.ReadChroma(ref bits, syntax, result, header.Picture, x / 2, y / 2, pattern >> 4, qp, prediction[256..]);
                    }
                    else
                    {
                        int intraType = header.Kind switch
                        {
                            H264SliceKind.I => type,
                            H264SliceKind.P => type - 5,
                            _ => throw new NotSupportedException("CABAC reconstruction supports I and P slices.")
                        };
                        if (intraType == 25)
                        {
                            syntax.ReadPcm(ref bits, header, address);
                        }
                        else
                        {
                            syntax.ReadPrediction(ref bits, header, address, intraType);
                            int pattern = syntax.ReadPattern(ref bits, address, intraType);
                            qp = syntax.ReadQuantizer(ref bits, intraType, pattern, qp);
                            result.Quantizers[address] = (byte)qp;
                            H264IntraReconstruction.Read(ref bits, syntax, result, header, x, y, intraType, state.ChromaModes[address], pattern, qp);
                        }
                    }
                    bool ended = reader.Termination(ref bits);
                    if (ended != (address == result.Quantizers.Length - 1))
                    {
                        throw new InvalidDataException("The H.264 CABAC slice does not end at the complete picture boundary.");
                    }
                }
                bits.ReadCabacPadding();
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
