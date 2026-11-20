// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal static class H264PictureDecoder
    {
        public static H264Picture Decode(ref H264BitReader bits, H264SliceHeader header,
            IReadOnlyList<H264ReferencePicture?> references, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (header.FirstMacroblock != 0)
            {
                throw new InvalidDataException("Single-slice reconstruction must start at macroblock zero.");
            }
            string? reason = H264Support.GetUnsupportedReason(header);
            if (reason is not null)
            {
                throw new NotSupportedException(reason);
            }
            if (header.Picture.Cabac)
            {
                return H264CabacPictureDecoder.Decode(ref bits, header, new(references, []), default, allocator, cancellationToken);
            }
            H264Picture result = new(header.Sequence, allocator, header.Kind, header.Structure);
            bool complete = false;
            try
            {
                DecodeSlice(ref bits, header, references, result, result.Quantizers.Length, cancellationToken);
                H264Deblocking.Apply(result, header, cancellationToken, references);
                result.CaptureReferences(0, references, []);
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
            IReadOnlyList<H264ReferencePicture?> references, H264Picture result, int endMacroblock,
            CancellationToken cancellationToken = default)
        {
            int qp = header.Qp;
            H264AdaptiveReferences? adaptive = null;
            if (header.Layout.MacroblockAdaptiveFrameField && header.Kind == H264SliceKind.P)
            {
                adaptive = new(new(references, []));
            }
            H264CavlcSliceCursor cursor = new(header.Layout, header.Kind, header.FirstMacroblock, endMacroblock);
            while (cursor.MoveNext(ref bits, result.Grid, out int address, out bool skipped))
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (header.Kind)
                {
                    case H264SliceKind.I:
                        H264IntraMacroblock.Read(ref bits, result, header, address, bits.ReadUe(25), ref qp);
                        break;
                    case H264SliceKind.P:
                        IReadOnlyList<H264ReferencePicture?> active = references;
                        if (adaptive is not null)
                        {
                            (int x, int y) = result.Grid.Position(address);
                            H264MacroblockPredictionContext context = H264MacroblockPredictionContext.Create(result, x, y);
                            active = adaptive.Get(context.Structure).L0;
                        }
                        H264InterMacroblock.Read(ref bits, result, header, active, address, ref qp, skipped);
                        break;
                    default:
                        throw new NotSupportedException("Only H.264 CAVLC I and P picture reconstruction is implemented.");
                }
            }
            bits.ReadTrailingBits();
        }
    }
}
