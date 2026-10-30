// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal static class H264CabacPictureDecoder
    {
        public static H264Picture Decode(ref H264BitReader bits, H264SliceHeader header,
            H264BidirectionalReferences references, H264PictureOrder order, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (header.FirstMacroblock != 0)
            {
                throw new InvalidDataException("Single-slice reconstruction must start at macroblock zero.");
            }
            string? reason = H264Support.GetUnsupportedReason(header);
            if (!header.Picture.Cabac || reason is not null)
            {
                throw new NotSupportedException(reason ?? "CABAC reconstruction requires a CABAC slice.");
            }
            H264Picture result = new(header.Sequence, allocator, header.Kind);
            bool complete = false;
            try
            {
                DecodeSlice(ref bits, header, references, order, result, result.Quantizers.Length, allocator, cancellationToken);
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

        public static void DecodeSlice(ref H264BitReader bits, H264SliceHeader header,
            H264BidirectionalReferences references, H264PictureOrder order, H264Picture result, int endMacroblock,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            using H264CabacMacroblockState state = new(header.Sequence, allocator, header.FirstMacroblock);
            H264CabacReader reader = new(ref bits, header);
            using H264CabacPredictionReader? predictionReader = header.Kind switch
            {
                H264SliceKind.I => null,
                H264SliceKind.P or H264SliceKind.B => new H264CabacPredictionReader(reader, result, allocator),
                _ => throw new NotSupportedException("CABAC reconstruction supports I, P and B slices.")
            };
            H264CabacPictureSyntax syntax = new(reader, result, state);
            int qp = header.Qp;
            Span<byte> prediction = stackalloc byte[384];
            for (int address = header.FirstMacroblock; address < endMacroblock; address++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int skipContext = state.SkipCondition(state.Left(address)) + state.SkipCondition(state.Top(address));
                bool skipped = header.Kind switch
                {
                    H264SliceKind.I => false,
                    H264SliceKind.P => H264CabacMacroblockSyntax.ReadSkip(ref bits, reader, skipContext),
                    H264SliceKind.B => H264CabacMacroblockSyntax.ReadBidirectionalSkip(ref bits, reader, skipContext),
                    _ => throw new NotSupportedException("CABAC reconstruction supports I, P and B slices.")
                };
                int type = header.Kind switch
                {
                    H264SliceKind.I => syntax.ReadType(ref bits, address),
                    H264SliceKind.P => skipped ? 0 : H264CabacMacroblockSyntax.ReadPredictiveType(ref bits, reader),
                    H264SliceKind.B => skipped ? 0 : H264CabacMacroblockSyntax.ReadBidirectionalType(ref bits, reader,
                        state.BidirectionalTypeCondition(state.Left(address)) + state.BidirectionalTypeCondition(state.Top(address))),
                    _ => throw new NotSupportedException("CABAC reconstruction supports I, P and B slices.")
                };
                int x = address % header.Sequence.WidthInMbs * 16;
                int y = address / header.Sequence.WidthInMbs * 16;
                bool inter = header.Kind switch
                {
                    H264SliceKind.I => false,
                    H264SliceKind.P => type < 5,
                    H264SliceKind.B => type < 23,
                    _ => throw new NotSupportedException("CABAC reconstruction supports I, P and B slices.")
                };
                if (inter)
                {
                    state.Types[address] = skipped ? H264CabacMacroblockState.Skipped : header.Kind switch
                    {
                        H264SliceKind.P => H264CabacMacroblockState.Inter,
                        H264SliceKind.B => type == 0 ? H264CabacMacroblockState.Direct : H264CabacMacroblockState.Inter,
                        _ => throw new InvalidDataException("The CABAC inter macroblock has an invalid slice kind.")
                    };
                    bool noSmallPartitions = true;
                    H264CabacPredictionReader interReader = predictionReader ?? throw new InvalidOperationException("The CABAC inter reader is missing.");
                    if (header.Kind == H264SliceKind.B)
                    {
                        noSmallPartitions = H264BidirectionalSyntax.Read(ref bits, interReader, result, header, references, order, x, y, type, prediction);
                    }
                    else if (skipped)
                    {
                        H264InterPrediction.Skip(result, header, references.L0, x, y, prediction);
                    }
                    else
                    {
                        noSmallPartitions = H264InterPrediction.Read(ref bits, interReader, result, header, references.L0, x, y, type, prediction);
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
                        H264SliceKind.B => type - 23,
                        _ => throw new NotSupportedException("CABAC reconstruction supports I, P and B slices.")
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
                if (ended != (address == endMacroblock - 1))
                {
                    throw new InvalidDataException("The H.264 CABAC slice does not end at its declared macroblock boundary.");
                }
            }
            bits.ReadCabacPadding();
        }
    }
}
