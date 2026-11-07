// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual
{
    internal partial class Mpeg4PictureDecoder
    {
        private void DecodeBidirectional(ref Mpeg4VisualBitReader bits, Mpeg4VopHeader header, Span<byte> picture, long time, CancellationToken token)
        {
            long interval = clock.ReferenceInterval, distance = time - (clock.LastReferenceTime - interval);
            if (interval <= 0 || distance <= 0 || distance >= interval) { throw new InvalidDataException("The MPEG-4 Visual B picture is outside its reference interval."); }
            int quantizer = header.Quantizer, bound = 0, forwardX = 0, forwardY = 0, backwardX = 0, backwardY = 0;
            Span<int> vectors = stackalloc int[16], coefficients = stackalloc int[64];
            for (int mb = 0; mb < mbWidth * mbHeight; mb++)
            {
                token.ThrowIfCancellationRequested();
                int priorBound = bound; ReadResynchronization(ref bits, header, mb, ref quantizer, ref bound);
                if (mb % mbWidth == 0 || bound != priorBound) { forwardX = forwardY = backwardX = backwardY = 0; }
                vectors.Clear(); int mode, cbp = 0;
                if (ReferenceMotion[mb * 10 + 8] == 16) { mode = 3; }
                else
                {
                    if (bits.Flag()) { mode = 4; }
                    else
                    {
                        bool noCoefficients = bits.Flag(); mode = 0;
                        while (!bits.Flag()) { if (++mode > 3) { throw new InvalidDataException("The MPEG-4 Visual B macroblock mode is invalid."); } }
                        if (!noCoefficients) { cbp = bits.Read(6); }
                        if (mode != 0 && cbp != 0 && bits.Flag()) { quantizer = Math.Clamp(quantizer + (bits.Flag() ? 2 : -2), 1, 31); }
                    }
                    if (mode is 0 or 4)
                    {
                        int deltaX = mode == 0 ? MotionDifference(ref bits, 1) : 0, deltaY = mode == 0 ? MotionDifference(ref bits, 1) : 0;
                        for (int i = 0; i < 8; i++)
                        {
                            int reference = ReferenceMotion[mb * 10 + i], delta = (i & 1) == 0 ? deltaX : deltaY;
                            vectors[i] = checked((int)(reference * distance / interval)) + delta;
                            vectors[8 + i] = delta == 0 ? checked((int)(reference * (distance - interval) / interval)) : vectors[i] - reference;
                        }
                    }
                    else
                    {
                        if (mode is 1 or 3)
                        {
                            forwardX = WrapMotion(forwardX + MotionDifference(ref bits, header.ForwardCode), header.ForwardCode);
                            forwardY = WrapMotion(forwardY + MotionDifference(ref bits, header.ForwardCode), header.ForwardCode);
                            for (int i = 0; i < 4; i++) { vectors[i * 2] = forwardX; vectors[i * 2 + 1] = forwardY; }
                        }
                        if (mode is 1 or 2)
                        {
                            backwardX = WrapMotion(backwardX + MotionDifference(ref bits, header.BackwardCode), header.BackwardCode);
                            backwardY = WrapMotion(backwardY + MotionDifference(ref bits, header.BackwardCode), header.BackwardCode);
                            for (int i = 0; i < 4; i++) { vectors[8 + i * 2] = backwardX; vectors[9 + i * 2] = backwardY; }
                        }
                    }
                }
                bool direct = mode is 0 or 4;
                if (mode != 2) { Predict(picture, previous!.Memory.Span, mb, vectors[..8], direct, false, false); }
                if (mode != 3) { Predict(picture, future!.Memory.Span, mb, vectors[8..], direct, false, mode != 2); }
                for (int block = 0; block < 6; block++)
                {
                    if ((cbp & (1 << (5 - block))) == 0) { continue; }
                    coefficients.Clear(); ulong nonzero = ReadResidual(ref bits, coefficients, false, 0, header.AlternateScan ? Mpeg4Scan.Vertical : Mpeg4Scan.Zigzag);
                    nonzero = Dequantize(coefficients, quantizer, false, 0, nonzero); (int offset, int stride) = BlockAddress(mb, block);
                    Mpeg4InverseDct.Write(coefficients, picture[offset..], stride, false, nonzero);
                }
            }
        }
    }
}
