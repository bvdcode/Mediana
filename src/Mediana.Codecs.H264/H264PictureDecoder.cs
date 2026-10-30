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
            H264Picture result = new(header.Sequence, allocator, header.Kind);
            bool complete = false;
            try
            {
                DecodeSlice(ref bits, header, references, result, result.Quantizers.Length, cancellationToken);
                H264Deblocking.Apply(result, header, cancellationToken, references);
                result.CaptureReferences(references, []);
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
            int address = header.FirstMacroblock;
            while (address < endMacroblock && bits.MoreRbspData())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (header.Kind == H264SliceKind.P)
                {
                    int skip = bits.ReadUe(endMacroblock - address);
                    for (int index = 0; index < skip; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        H264InterMacroblock.Read(ref bits, result, header, references, address++, ref qp, skip: true);
                    }
                    if (address == endMacroblock || (skip > 0 && !bits.MoreRbspData()))
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
            if (address != endMacroblock)
            {
                throw new InvalidDataException("The H.264 slice does not cover its declared macroblock range.");
            }
            bits.ReadTrailingBits();
        }
    }
}
