// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264CabacPictureEncoder
    {
        public static MediaBuffer? Encode(VideoFrame input, H264Picture target, H264Picture? reference,
            bool keyFrame, int frameNumber, int idrId, int quantizer, MediaBufferAllocator allocator,
            H264EncoderOptions options, int maximumBytes, out int encodedLength, CancellationToken token,
            H264MotionReference? interpolation)
        {
            using H264BitWriter bits = new(allocator, maximumBytes, keyFrame ? (byte)0x65 : (byte)0x41,
                lengthPrefixed: true, measureOverflow: true);
            bits.WriteUnsigned(0); bits.WriteUnsigned(keyFrame ? 7U : 5U); bits.WriteUnsigned(0);
            bits.WriteBits((uint)frameNumber, 4);
            if (keyFrame) { bits.WriteUnsigned((uint)idrId); }
            else { bits.WriteFlag(false); }
            bits.WriteFlag(false); bits.WriteFlag(false);
            if (!keyFrame) { bits.WriteUnsigned(0); }
            bits.WriteSigned(quantizer - options.Quantizer); bits.WriteUnsigned(1);
            while ((bits.BitPosition & 7) != 0) { bits.WriteFlag(true); }
            H264CabacEncoder arithmetic = new(bits);
            using H264CabacMacroblockState state = new(target.Sequence, allocator);
            H264CabacEncodingSyntax syntax = new(arithmetic, state, target, keyFrame ? H264SliceKind.I : H264SliceKind.P, quantizer);
            using H264CabacPredictionWindow? motion = keyFrame ? null : new(target.Width, target.Height, false, allocator);
            switch (options.MacroblockAnalysis)
            {
                case H264MacroblockAnalysis.Fast:
                    WriteBlocks<H264FastMacroblockAnalysis>(input, target, reference, keyFrame, quantizer,
                        options.MotionSearchRange, syntax, arithmetic, motion, token, interpolation, null);
                    break;
                case H264MacroblockAnalysis.RateDistortion when keyFrame:
                    WriteBlocks<H264FastMacroblockAnalysis>(input, target, reference, true, quantizer,
                        options.MotionSearchRange, syntax, arithmetic, motion, token, interpolation, null);
                    break;
                case H264MacroblockAnalysis.RateDistortion:
                    H264RateDistortionAnalysis analysis = new(state, target, quantizer);
                    WriteBlocks<H264DetailedMacroblockAnalysis>(input, target, reference, false, quantizer,
                        options.MotionSearchRange, syntax, arithmetic, motion, token, interpolation, analysis);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(options));
            }
            token.ThrowIfCancellationRequested();
            MediaBuffer? result = bits.TryFinishCabac(arithmetic.BinCount, target.Quantizers.Length, 384 * 8);
            encodedLength = bits.EncodedLength;
            return result;
        }

        private static void WriteBlocks<TAnalysis>(VideoFrame input, H264Picture target, H264Picture? reference,
            bool keyFrame, int quantizer, int searchRange, H264CabacEncodingSyntax syntax, H264CabacEncoder arithmetic,
            H264CabacPredictionWindow? motion, CancellationToken token, H264MotionReference? interpolation,
            H264RateDistortionAnalysis? analysis) where TAnalysis : struct, IH264MacroblockAnalysisStrategy
        {
            Span<byte> samples = stackalloc byte[H264SourceMacroblock.ByteCount];
            Span<byte> prediction = stackalloc byte[384];
            Span<int> coefficients = stackalloc int[256 + H264ChromaEncoder.CoefficientCount];
            int address = 0;
            for (int y = 0; y < target.Height; y += 16)
            {
                for (int x = 0; x < target.Width; x += 16, address++)
                {
                    token.ThrowIfCancellationRequested();
                    H264SourceMacroblock source = new(input, x, y, samples);
                    if (keyFrame) { WriteIntra(syntax, source, target, address, x, y, quantizer, prediction, coefficients); }
                    else
                    {
                        WriteInter<TAnalysis>(syntax, source, target, reference!, motion!, address, x, y, quantizer,
                            searchRange, prediction, coefficients, token, interpolation, analysis);
                    }
                    target.Quantizers[address] = (byte)quantizer;
                    arithmetic.WriteTermination(address == target.Quantizers.Length - 1);
                }
            }
        }

        private static void WriteIntra(H264CabacEncodingSyntax syntax, H264SourceMacroblock source, H264Picture target,
            int address, int x, int y, int quantizer, Span<byte> prediction, Span<int> coefficients)
        {
            syntax.WriteIntraType(address);
            int pattern = H264IntraLumaEncoder.Analyze(syntax, source.Luma, target, x, y, quantizer, coefficients[..256]);
            for (int plane = 1; plane <= 2; plane++)
            {
                target.PredictMacroblock(plane, x / 2, y / 2, 0, prediction.Slice((plane - 1) * 64, 64));
            }
            int chroma = H264ChromaEncoder.Analyze(source.Chroma, quantizer, prediction[..128], coefficients[256..]);
            syntax.WriteChromaMode(address);
            syntax.WritePattern(address, pattern | (chroma << 4));
            if (pattern != 0 || chroma != 0) { syntax.WriteQuantizerDelta(0); }
            H264IntraLumaEncoder.TryWrite(syntax, target, x, y, pattern, coefficients[..256]);
            H264ChromaEncoder.TryWrite(syntax, target, x / 2, y / 2, quantizer, chroma, prediction[..128], coefficients[256..]);
        }

        private static void WriteInter<TAnalysis>(H264CabacEncodingSyntax syntax, H264SourceMacroblock source, H264Picture target,
            H264Picture reference, H264CabacPredictionWindow motion, int address, int x, int y, int quantizer,
            int searchRange, Span<byte> prediction, Span<int> coefficients, CancellationToken token, H264MotionReference? interpolation, H264RateDistortionAnalysis? analysis)
            where TAnalysis : struct, IH264MacroblockAnalysisStrategy
        {
            motion.BeginMacroblock(address);
            (int motionX, int motionY, bool exact) = H264MotionSearch.Select(source, reference, target, x, y,
                searchRange, prediction, token, interpolation);
            (int skipX, int skipY) = H264MotionPrediction.Skip(target, x, y);
            int pattern = 0;
            if (exact) { coefficients.Clear(); }
            else
            {
                pattern = H264InterEncoder.AnalyzeLuma(source.Luma, quantizer, prediction[..256], coefficients[..256]);
                pattern |= H264ChromaEncoder.Analyze(source.Chroma, quantizer, prediction[256..], coefficients[256..]) << 4;
            }
            if (TAnalysis.Enabled && analysis!.TryWrite(syntax, source, motion, x, y, pattern,
                motionX, motionY, prediction, coefficients)) { return; }
            bool skipped = pattern == 0 && motionX == skipX && motionY == skipY;
            syntax.WriteSkip(address, skipped);
            if (!skipped)
            {
                syntax.WritePredictiveType();
                (int predictedX, int predictedY) = H264MotionPrediction.Predict(target, H264ReferenceList.L0, x, y, 16, 16, 0, 0);
                WriteMotion(syntax, motion, target, x, y, motionX - predictedX, false);
                WriteMotion(syntax, motion, target, x, y, motionY - predictedY, true);
                syntax.WritePattern(address, pattern);
                if (pattern != 0) { syntax.WriteQuantizerDelta(0); }
            }
            target.SetMotion(H264ReferenceList.L0, x, y, 16, 16, 0, motionX, motionY);
            H264InterEncoder.TryWriteLuma(syntax, target, x, y, quantizer, pattern, prediction[..256], coefficients[..256]);
            H264ChromaEncoder.TryWrite(syntax, target, x / 2, y / 2, quantizer, pattern >> 4, prediction[256..], coefficients[256..]);
        }

        private static void WriteMotion(H264CabacEncodingSyntax syntax, H264CabacPredictionWindow window,
            H264Picture target, int x, int y, int value, bool vertical)
        {
            Span<short> history = vertical ? window.MotionY(H264ReferenceList.L0) : window.MotionX(H264ReferenceList.L0);
            int sum = target.IsInCurrentSlice(x - 1, y) ? Math.Abs((int)history[window.BlockIndex(x - 1, y)]) : 0;
            if (target.IsInCurrentSlice(x, y - 1)) { sum += Math.Abs((int)history[window.BlockIndex(x, y - 1)]); }
            syntax.WriteMotionDifference(value, vertical, sum);
            for (int row = y; row < y + 16; row += 4)
            {
                history.Slice(window.BlockIndex(x, row), 4).Fill((short)value);
            }
        }
    }
}
