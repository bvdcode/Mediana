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
            using H264BitWriter bits = new(allocator, maximumBytes, 0x65, lengthPrefixed: true, measureOverflow: true);
            bits.WriteUnsigned(0);
            bits.WriteUnsigned(7);
            bits.WriteUnsigned(0);
            bits.WriteBits(0, 4);
            bits.WriteUnsigned((uint)idrId);
            bits.WriteFlag(false);
            bits.WriteFlag(false);
            bits.WriteSigned(qp - initialQuantizer);
            bits.WriteUnsigned(1);
            int previousQuantizer = qp;
            Span<byte> samples = stackalloc byte[H264SourceMacroblock.ByteCount];
            for (int y = 0; y < reconstructed.Height; y += 16)
            {
                for (int x = 0; x < reconstructed.Width; x += 16)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    H264SourceMacroblock source = new(input, x, y, samples);
                    int quantizer = qp;
                    while (true)
                    {
                        H264BitWriterCheckpoint checkpoint = bits.CreateCheckpoint();
                        bool written = TryWriteMacroblock(bits, source, reconstructed, x, y, quantizer, previousQuantizer, out int effectiveQuantizer);
                        if (written && H264MacroblockEncoding.Fits(bits, checkpoint))
                        {
                            previousQuantizer = effectiveQuantizer;
                            break;
                        }
                        if (written && H264PcmMacroblockEncoder.TryWrite(bits, checkpoint, source, reconstructed, x, y, H264SliceKind.I))
                        {
                            break;
                        }
                        bits.Restore(checkpoint);
                        for (int row = 0; row < 16; row += 4)
                        {
                            reconstructed.Modes.Slice(reconstructed.BlockIndex(0, x, y + row), 4).Fill(byte.MaxValue);
                        }
                        quantizer = H264MacroblockEncoding.IncreaseQuantizer(quantizer);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    if (!measureOverflow && bits.EncodedLength > maximumBytes)
                    {
                        encodedLength = bits.EncodedLength;
                        return null;
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            MediaBuffer? result = bits.TryFinish();
            encodedLength = bits.EncodedLength;
            return result;
        }

        private static bool TryWriteMacroblock(H264BitWriter bits, H264SourceMacroblock source, H264Picture reconstructed, int x, int y,
            int qp, int previousQuantizer, out int effectiveQuantizer)
        {
            bits.WriteUnsigned(0);
            Span<int> luma = stackalloc int[256];
            int pattern = H264IntraLumaEncoder.Analyze(bits, source.Luma, reconstructed, x, y, qp, luma);
            Span<byte> prediction = stackalloc byte[128];
            Span<int> coefficients = stackalloc int[H264ChromaEncoder.CoefficientCount];
            for (int plane = 1; plane <= 2; plane++)
            {
                reconstructed.PredictMacroblock(plane, x / 2, y / 2, 0, prediction.Slice((plane - 1) * 64, 64));
            }
            int chromaPattern = H264ChromaEncoder.Analyze(source.Chroma, qp, prediction, coefficients);
            bits.WriteUnsigned(0);
            bits.WriteUnsigned((uint)H264IntraMacroblock.CodedPatterns.IndexOf((byte)(pattern | (chromaPattern << 4))));
            effectiveQuantizer = previousQuantizer;
            if (pattern != 0 || chromaPattern != 0)
            {
                H264MacroblockEncoding.WriteQuantizerDelta(bits, previousQuantizer, qp);
                effectiveQuantizer = qp;
            }
            reconstructed.Quantizers[y / 16 * reconstructed.Sequence.WidthInMbs + x / 16] = (byte)effectiveQuantizer;
            return H264IntraLumaEncoder.TryWrite(bits, reconstructed, x, y, pattern, luma) &&
                H264ChromaEncoder.TryWrite(bits, reconstructed, x / 2, y / 2, qp, chromaPattern, prediction, coefficients);
        }
    }
}
