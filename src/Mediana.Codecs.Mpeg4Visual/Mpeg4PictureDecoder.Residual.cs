// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual
{
    internal partial class Mpeg4PictureDecoder
    {
        private static void ReadResidual(ref Mpeg4VisualBitReader bits, scoped Span<int> coefficients, bool intra, int start, scoped ReadOnlySpan<byte> scan)
        {
            int position = start;
            while (true)
            {
                int item = Mpeg4Codebooks.Coefficient(ref bits, intra); position += (item >> 16) & 255;
                if (position >= 64) { throw new InvalidDataException("An MPEG-4 Visual coefficient run exceeds its block."); }
                coefficients[scan[position++]] = (short)item;
                if ((item >> 24) != 0) { return; }
            }
        }
        private void ReadIntra(ref Mpeg4VisualBitReader bits, scoped Span<int> coefficients, int mb, int block, int quantizer,
            bool acPrediction, int cbp, Mpeg4VopHeader header, int bound)
        {
            int scaler = DcScale(quantizer, block < 4), start = 0;
            if (quantizer < header.IntraDcThreshold)
            {
                int size = (block < 4 ? Mpeg4Codebooks.DcLuma : Mpeg4Codebooks.DcChroma).Read(ref bits);
                int value = bits.Read(size);
                if (size != 0 && (value & (1 << (size - 1))) == 0) { value -= (1 << size) - 1; }
                if (size > 8) { bits.Marker(); } coefficients[0] = value; start = 1;
            }
            int plane = block < 4 ? 0 : block - 3, width = plane == 0 ? mbWidth * 2 : mbWidth;
            int x = mb % mbWidth, y = mb / mbWidth;
            if (plane == 0) { x = x * 2 + (block & 1); y = y * 2 + (block >> 1); }
            int baseIndex = plane switch { 0 => 0, 1 => mbWidth * mbHeight * 4, 2 => mbWidth * mbHeight * 5,
                _ => throw new InvalidOperationException("Invalid MPEG-4 Visual plane.") };
            int index = baseIndex + y * width + x;
            int left = x != 0 && NeighborAvailable(plane, x - 1, y, bound) ? index - 1 : -1;
            int top = y != 0 && NeighborAvailable(plane, x, y - 1, bound) ? index - width : -1;
            int diagonal = x != 0 && y != 0 && NeighborAvailable(plane, x - 1, y - 1, bound) ? index - width - 1 : -1;
            Span<int> predictions = Predictions;
            int leftDc = left < 0 ? 1024 : predictions[left * 16], topDc = top < 0 ? 1024 : predictions[top * 16];
            int diagonalDc = diagonal < 0 ? 1024 : predictions[diagonal * 16];
            bool vertical = Math.Abs(leftDc - diagonalDc) < Math.Abs(diagonalDc - topDc);
            ReadOnlySpan<byte> scan = header.AlternateScan ? Mpeg4Scan.Vertical : !acPrediction ? Mpeg4Scan.Zigzag
                : vertical ? Mpeg4Scan.Horizontal : Mpeg4Scan.Vertical;
            if ((cbp & (1 << (5 - block))) != 0) { ReadResidual(ref bits, coefficients, true, start, scan); }
            coefficients[0] += RoundDivide(vertical ? topDc : leftDc, scaler);
            int neighbor = vertical ? top : left;
            if (acPrediction && neighbor >= 0)
            {
                int q = predictions[neighbor * 16 + 15];
                for (int i = 1; i < 8; i++)
                {
                    int ac = predictions[neighbor * 16 + (vertical ? i : i + 7)];
                    coefficients[vertical ? i : i * 8] += RoundDivide(ac * q, quantizer);
                }
            }
            predictions[index * 16] = Math.Clamp(coefficients[0] * scaler, -2048, 2047);
            predictions[index * 16 + 15] = quantizer;
            for (int i = 1; i < 8; i++)
            { predictions[index * 16 + i] = coefficients[i]; predictions[index * 16 + i + 7] = coefficients[i * 8]; }
        }
        private bool NeighborAvailable(int plane, int x, int y, int bound)
        {
            int mb = plane == 0 ? (y / 2) * mbWidth + x / 2 : y * mbWidth + x;
            if (mb < bound) { return false; }
            int offset = plane switch { 0 => 0, 1 => mbWidth * mbHeight * 4, 2 => mbWidth * mbHeight * 5,
                _ => throw new InvalidOperationException("Invalid MPEG-4 Visual plane.") };
            return Predictions[(offset + y * (plane == 0 ? mbWidth * 2 : mbWidth) + x) * 16 + 15] != 0;
        }
        private static int RoundDivide(int value, int divisor) => value >= 0 ? (value + divisor / 2) / divisor : (value - divisor / 2) / divisor;
        private static int DcScale(int quantizer, bool luma)
        {
            if (quantizer < 5) { return 8; }
            if (!luma) { return quantizer < 25 ? (quantizer + 13) / 2 : quantizer - 6; }
            if (quantizer < 9) { return quantizer * 2; }
            return quantizer < 25 ? quantizer + 8 : quantizer * 2 - 16;
        }
        private void Dequantize(scoped Span<int> coefficients, int quantizer, bool intra, int dcScale)
        {
            int sum = 0, start = 0;
            if (intra) { coefficients[0] = Math.Clamp(coefficients[0] * dcScale, -2048, 2047); start = 1; }
            ReadOnlySpan<byte> matrix = intra ? config.IntraMatrix.Span : config.InterMatrix.Span;
            for (int i = start; i < 64; i++)
            {
                int value = coefficients[i], magnitude = Math.Abs(value);
                if (magnitude == 0) { continue; }
                if (config.MpegQuantization)
                { magnitude = intra ? magnitude * matrix[i] * quantizer / 8 : (magnitude * 2 + 1) * matrix[i] * quantizer / 16; }
                else { magnitude = magnitude * quantizer * 2 + ((quantizer - 1) | 1); }
                coefficients[i] = Math.Clamp(value < 0 ? -magnitude : magnitude, -2048, 2047); sum ^= coefficients[i];
            }
            if (config.MpegQuantization && !intra && (sum & 1) == 0) { coefficients[63] ^= 1; }
        }
        private void ReadResynchronization(ref Mpeg4VisualBitReader bits, Mpeg4VopHeader header, int mb, ref int quantizer, ref int bound)
        {
            if (config.ResyncMarkerDisabled) { return; }
            int alignment = 8 - (int)(bits.BitPosition & 7);
            int length = 17 + (header.Type switch
            {
                Mpeg4VopType.Intra => 0, Mpeg4VopType.Predictive => header.ForwardCode - 1,
                Mpeg4VopType.Bidirectional => Math.Max(2, Math.Max(header.ForwardCode, header.BackwardCode)) - 1,
                Mpeg4VopType.Sprite => throw new NotSupportedException("Sprite resynchronization is not supported."),
                _ => throw new InvalidDataException("Invalid MPEG-4 Visual picture type.")
            });
            if (bits.RemainingBits < alignment + length || bits.Peek(alignment) != (1 << (alignment - 1)) - 1) { return; }
            Mpeg4VisualBitReader look = bits; look.Skip(alignment);
            if (look.Read(length) != 1) { return; } bits = look;
            int numberBits = Math.Max(1, System.Numerics.BitOperations.Log2((uint)(mbWidth * mbHeight - 1)) + 1);
            if (bits.Read(numberBits) != mb) { throw new InvalidDataException("The MPEG-4 Visual video-packet macroblock address is inconsistent."); }
            quantizer = bits.Read(config.QuantizerBits);
            if (quantizer == 0) { throw new InvalidDataException("The MPEG-4 Visual video-packet quantizer is zero."); }
            if (bits.Flag())
            {
                int modulo = 0; while (bits.Flag()) { modulo = checked(modulo + 1); }
                bits.Marker(); int increment = bits.Read(config.TimeIncrementBits); bits.Marker();
                Mpeg4VopType type = (Mpeg4VopType)bits.Read(2); ReadOnlySpan<byte> thresholds = [32,13,15,17,19,21,23,1];
                int threshold = thresholds[bits.Read(3)], forward = type != Mpeg4VopType.Intra ? bits.Read(3) : 1;
                int backward = type == Mpeg4VopType.Bidirectional ? bits.Read(3) : 1;
                if (modulo != header.ModuloTimeBase || increment != header.TimeIncrement || type != header.Type || threshold != header.IntraDcThreshold ||
                    forward != header.ForwardCode || backward != header.BackwardCode)
                { throw new InvalidDataException("The MPEG-4 Visual video-packet header contradicts its picture header."); }
            }
            bound = mb;
        }
    }
}
