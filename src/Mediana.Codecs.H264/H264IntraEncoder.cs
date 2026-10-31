// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264IntraEncoder
    {
        public static MediaBuffer? Encode(VideoFrame input, H264Picture reconstructed, int qp, int initialQuantizer, int idrId,
            MediaBufferAllocator allocator, int maximumBytes, bool measureOverflow, out int encodedLength, CancellationToken cancellationToken)
        {
            using H264BitWriter bits = new(allocator, maximumBytes, 0x65, lengthPrefixed: true, measureOverflow: measureOverflow);
            bits.WriteUnsigned(0);
            bits.WriteUnsigned(7);
            bits.WriteUnsigned(0);
            bits.WriteBits(0, 4);
            bits.WriteUnsigned((uint)idrId);
            bits.WriteFlag(false);
            bits.WriteFlag(false);
            bits.WriteSigned(qp - initialQuantizer);
            bits.WriteUnsigned(1);
            for (int y = 0; y < reconstructed.Height; y += 16)
            {
                for (int x = 0; x < reconstructed.Width; x += 16)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    WriteMacroblock(bits, input, reconstructed, x, y, qp);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            MediaBuffer? result = bits.TryFinish();
            encodedLength = bits.EncodedLength;
            return result;
        }

        private static void WriteMacroblock(H264BitWriter bits, VideoFrame input, H264Picture reconstructed, int x, int y, int qp)
        {
            bits.WriteUnsigned(0);
            Span<int> luma = stackalloc int[256];
            int pattern = H264IntraLumaEncoder.Analyze(bits, input, reconstructed, x, y, qp, luma);
            Span<byte> prediction = stackalloc byte[128];
            Span<int> coefficients = stackalloc int[H264ChromaEncoder.CoefficientCount];
            for (int plane = 1; plane <= 2; plane++)
            {
                reconstructed.PredictMacroblock(plane, x / 2, y / 2, 0, prediction.Slice((plane - 1) * 64, 64));
            }
            int chromaPattern = H264ChromaEncoder.Analyze(input, x / 2, y / 2, qp, prediction, coefficients);
            bits.WriteUnsigned(0);
            bits.WriteUnsigned((uint)H264IntraMacroblock.CodedPatterns.IndexOf((byte)(pattern | (chromaPattern << 4))));
            if (pattern != 0 || chromaPattern != 0)
            {
                bits.WriteSigned(0);
            }
            reconstructed.Quantizers[y / 16 * reconstructed.Sequence.WidthInMbs + x / 16] = (byte)qp;
            H264IntraLumaEncoder.Write(bits, reconstructed, x, y, pattern, luma);
            H264ChromaEncoder.Write(bits, reconstructed, x / 2, y / 2, qp, chromaPattern, prediction, coefficients);
        }
    }
}
