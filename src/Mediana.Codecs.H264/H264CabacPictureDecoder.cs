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
            H264Picture result = new(header.Sequence, allocator, header.Kind, header.Structure);
            bool complete = false;
            try
            {
                DecodeSlice(ref bits, header, references, order, result, result.Quantizers.Length, allocator, cancellationToken);
                H264Deblocking.Apply(result, header, cancellationToken, references.L0, references.L1);
                result.CaptureReferences(0, references.L0, references.L1);
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
            if (header.FirstMacroblock < 0 || endMacroblock <= header.FirstMacroblock || endMacroblock > header.Layout.MacroblockCount)
            {
                throw new ArgumentOutOfRangeException(nameof(endMacroblock));
            }
            if (header.Layout.MacroblockAdaptiveFrameField && ((header.FirstMacroblock | endMacroblock) & 1) != 0)
            {
                throw new InvalidDataException("An MBAFF slice must contain complete macroblock pairs.");
            }
            using H264CabacMacroblockState state = new(result, allocator, header.FirstMacroblock);
            H264CabacReader reader = new(ref bits, header);
            using H264CabacPredictionReader? predictionReader = header.Kind switch
            {
                H264SliceKind.I => null,
                H264SliceKind.P or H264SliceKind.B => new H264CabacPredictionReader(reader, result, allocator),
                _ => throw new NotSupportedException("CABAC reconstruction supports I, P and B slices.")
            };
            H264CabacPictureSyntax syntax = new(reader, result, state);
            H264CabacPairSyntax pairSyntax = new(result, header, reader, state);
            H264AdaptiveReferences? adaptive = header.Layout.MacroblockAdaptiveFrameField && header.Kind != H264SliceKind.I
                ? new(references) : null;
            int qp = header.Qp;
            Span<byte> prediction = stackalloc byte[result.MacroblockBytes];
            for (int address = header.FirstMacroblock; address < endMacroblock; address++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool skipped = pairSyntax.ReadSkip(ref bits, address);
                predictionReader?.BeginMacroblock(address);
                int type = header.Kind switch
                {
                    H264SliceKind.I => syntax.ReadType(ref bits, address),
                    H264SliceKind.P => skipped ? 0 : H264CabacMacroblockSyntax.ReadPredictiveType(ref bits, reader),
                    H264SliceKind.B => skipped ? 0 : H264CabacMacroblockSyntax.ReadBidirectionalType(ref bits, reader,
                        state.BidirectionalTypeCondition(state.Left(address)) + state.BidirectionalTypeCondition(state.Top(address))),
                    _ => throw new NotSupportedException("CABAC reconstruction supports I, P and B slices.")
                };
                (int x, int y) = result.Grid.Position(address);
                H264BidirectionalReferences selected = adaptive is null ? references
                    : adaptive.Get(H264MacroblockPredictionContext.Create(result, x, y).Structure);
                bool inter = header.Kind switch
                {
                    H264SliceKind.I => false,
                    H264SliceKind.P => type < 5,
                    H264SliceKind.B => type < 23,
                    _ => throw new NotSupportedException("CABAC reconstruction supports I, P and B slices.")
                };
                if (skipped)
                {
                    state.Types[address] = H264CabacMacroblockState.Skipped;
                    switch (header.Kind)
                    {
                        case H264SliceKind.P:
                            H264InterPrediction.ReconstructSkip(result, header, selected.L0, x, y, prediction);
                            break;
                        case H264SliceKind.B:
                            H264DirectPrediction.PredictMacroblock(result, header, selected, order, x, y, prediction);
                            result.WriteInterPrediction(x, y, prediction);
                            break;
                        default:
                            throw new InvalidDataException("Only P and B slices can contain skipped macroblocks.");
                    }
                    syntax.ResetQuantizerDelta();
                    result.Quantizers[address] = (byte)qp;
                }
                else if (inter)
                {
                    state.Types[address] = header.Kind switch
                    {
                        H264SliceKind.P => H264CabacMacroblockState.Inter,
                        H264SliceKind.B => type == 0 ? H264CabacMacroblockState.Direct : H264CabacMacroblockState.Inter,
                        _ => throw new InvalidDataException("The CABAC inter macroblock has an invalid slice kind.")
                    };
                    bool noSmallPartitions = true;
                    H264CabacPredictionReader interReader = predictionReader ?? throw new InvalidOperationException("The CABAC inter reader is missing.");
                    if (header.Kind == H264SliceKind.B)
                    {
                        noSmallPartitions = H264BidirectionalSyntax.Read(ref bits, interReader, result, header, selected, order, x, y, type, prediction);
                    }
                    else
                    {
                        noSmallPartitions = H264InterPrediction.Read(ref bits, interReader, result, header, selected.L0, x, y, type, prediction);
                    }
                    int pattern = syntax.ReadPattern(ref bits, address, 0);
                    if ((pattern & 15) != 0 && header.Picture.Transform8x8 && noSmallPartitions)
                    {
                        syntax.ReadTransform(ref bits, address);
                    }
                    qp = syntax.ReadQuantizer(ref bits, 0, pattern, qp);
                    result.Quantizers[address] = (byte)qp;
                    if (pattern == 0)
                    {
                        result.WriteInterPrediction(x, y, prediction);
                    }
                    else
                    {
                        H264CabacResidualReader residuals = new(reader, result, state, address);
                        H264Residuals.ReadInter(ref bits, residuals, result, header.Picture, x, y, pattern, qp, prediction);
                    }
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
                        syntax.ReadPcm(ref bits, address);
                    }
                    else
                    {
                        syntax.ReadPrediction(ref bits, header, address, intraType);
                        int pattern = syntax.ReadPattern(ref bits, address, intraType);
                        qp = syntax.ReadQuantizer(ref bits, intraType, pattern, qp);
                        result.Quantizers[address] = (byte)qp;
                        H264CabacResidualReader residuals = new(reader, result, state, address);
                        H264IntraReconstruction.Read(ref bits, residuals, result, header, x, y, intraType, state.ChromaModes[address], pattern, qp);
                    }
                }
                bool ended = (!header.Layout.MacroblockAdaptiveFrameField || (address & 1) != 0) && reader.Termination(ref bits);
                if (ended != (address == endMacroblock - 1))
                {
                    throw new InvalidDataException("The H.264 CABAC slice does not end at its declared macroblock boundary.");
                }
            }
            bits.ReadCabacPadding();
        }
    }
}
