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
            H264MotionReference? interpolation, H264StationaryHistory? stationary)
        {
            int lumaOffset = !keyFrame && H264LumaPredictionOffset.IsEnabled(options)
                ? H264LumaPredictionOffset.Estimate(input, reference!, token) : 0;
            using H264BitWriter bits = new(allocator, maximumBytes, keyFrame ? (byte)0x65 : (byte)0x41,
                lengthPrefixed: true, measureOverflow: true);
            WriteHeader(bits, keyFrame, frameNumber, idrId, lumaOffset, quantizer, options);
            while ((bits.BitPosition & 7) != 0) { bits.WriteFlag(true); }
            H264CabacEncoder arithmetic = new(bits);
            using H264CabacMacroblockState state = new(target, allocator);
            H264CabacEncodingSyntax syntax = new(arithmetic, state, target, keyFrame ? H264SliceKind.I : H264SliceKind.P, quantizer);
            using H264CabacPredictionWindow? motion = keyFrame ? null : new(target.Width, target.Height, false, allocator);
            switch (options.MacroblockAnalysis)
            {
                case H264MacroblockAnalysis.Fast:
                    WriteBlocks<H264FastMacroblockAnalysis>(input, target, reference, keyFrame, quantizer,
                        options.MotionSearchRange, syntax, arithmetic, motion, token, interpolation, null, stationary,
                        compareMotionRate: options.RateControl is null, lumaOffset: lumaOffset);
                    break;
                case H264MacroblockAnalysis.RateDistortion when keyFrame:
                    WriteBlocks<H264FastMacroblockAnalysis>(input, target, reference, true, quantizer,
                        options.MotionSearchRange, syntax, arithmetic, motion, token, interpolation, null, null);
                    break;
                case H264MacroblockAnalysis.RateDistortion:
                    H264RateDistortionAnalysis analysis = new(state, target, quantizer);
                    WriteBlocks<H264DetailedMacroblockAnalysis>(input, target, reference, false, quantizer,
                        options.MotionSearchRange, syntax, arithmetic, motion, token, interpolation, analysis, null);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(options));
            }
            token.ThrowIfCancellationRequested();
            MediaBuffer? result = bits.TryFinishCabac(arithmetic.BinCount, target.Quantizers.Length, 384 * 8);
            encodedLength = bits.EncodedLength;
            return result;
        }

        private static void WriteHeader(H264BitWriter bits, bool keyFrame, int frameNumber, int idrId,
            int lumaOffset, int quantizer, H264EncoderOptions options)
        {
            bits.WriteUnsigned(0);
            bits.WriteUnsigned(keyFrame ? 7U : 5U);
            bits.WriteUnsigned(lumaOffset == 0 ? 0 : H264EncoderConfiguration.WeightedPictureId);
            bits.WriteBits((uint)frameNumber, 4);
            if (keyFrame)
            {
                bits.WriteUnsigned((uint)idrId);
                bits.WriteFlag(false);
                bits.WriteFlag(false);
            }
            else
            {
                bits.WriteFlag(false);
                bits.WriteFlag(false);
                if (lumaOffset != 0)
                {
                    H264LumaPredictionOffset.WriteTable(bits, lumaOffset);
                }
                bits.WriteFlag(false);
                bits.WriteUnsigned(0);
            }
            bits.WriteSigned(quantizer - options.Quantizer);
            H264EncoderDeblocking.WriteHeader(bits, options.Deblocking);
        }

        private static void WriteBlocks<TAnalysis>(VideoFrame input, H264Picture target, H264Picture? reference,
            bool keyFrame, int quantizer, int searchRange, H264CabacEncodingSyntax syntax, H264CabacEncoder arithmetic,
            H264CabacPredictionWindow? motion, CancellationToken token, H264MotionReference? interpolation,
            H264RateDistortionAnalysis? analysis, H264StationaryHistory? stationary, bool compareMotionRate = false, int lumaOffset = 0)
            where TAnalysis : struct, IH264MacroblockAnalysisStrategy
        {
            H264SourcePicture sourcePicture = new(input);
            Span<byte> samples = stackalloc byte[H264SourceMacroblock.ByteCount];
            Span<byte> prediction = stackalloc byte[384];
            Span<int> coefficients = stackalloc int[256 + H264ChromaEncoder.CoefficientCount];
            H264SkipResidualBounds bounds = H264ForwardTransform.GetSkipBounds(quantizer);
            int address = 0;
            for (int y = 0; y < target.Height; y += 16)
            {
                for (int x = 0; x < target.Width; x += 16, address++)
                {
                    token.ThrowIfCancellationRequested();
                    int run = lumaOffset == 0 ? stationary?.MatchingRun(sourcePicture, address, x, y) ?? 0 : 0;
                    if (run != 0 && IsStationaryPredictor(target, stationary, address, x, y))
                    {
                        WriteStationaryRun(syntax, arithmetic, motion!, target, reference!, x, y, address, run, quantizer, token);
                        x += (run - 1) * 16;
                        address += run - 1;
                        continue;
                    }
                    H264SourceMacroblock source = new(sourcePicture, x, y, samples);
                    if (keyFrame) { WriteIntra(syntax, source, target, address, x, y, quantizer, prediction, coefficients); }
                    else
                    {
                        WriteInter<TAnalysis>(syntax, source, target, reference!, motion!, address, x, y, quantizer,
                            searchRange, prediction, coefficients, token, interpolation, analysis, bounds, stationary, compareMotionRate, lumaOffset);
                    }
                    target.Quantizers[address] = (byte)quantizer;
                    arithmetic.WriteTermination(address == target.Quantizers.Length - 1);
                }
            }
        }

        private static bool IsStationaryPredictor(H264Picture target, H264StationaryHistory? history, int address, int x, int y) =>
            x == 0 || y == 0 || history?.HasStationaryLeft(address) == true || H264MotionPrediction.Skip(target, x, y) == (0, 0);

        private static void WriteStationaryRun(H264CabacEncodingSyntax syntax, H264CabacEncoder arithmetic,
            H264CabacPredictionWindow motion, H264Picture target, H264Picture reference, int x, int y,
            int address, int count, int quantizer, CancellationToken token)
        {
            motion.BeginMacroblock(address);
            target.WriteStationarySkip(reference, x, y, count);
            Span<byte> quantizers = target.Quantizers;
            quantizers.Slice(address, count).Fill((byte)quantizer);
            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                syntax.WriteSkip(address + i, true);
                arithmetic.WriteTermination(address + i == quantizers.Length - 1);
            }
        }

        private static void WriteIntra(H264CabacEncodingSyntax syntax, H264SourceMacroblock source, H264Picture target,
            int address, int x, int y, int quantizer, Span<byte> prediction, Span<int> coefficients)
        {
            if (!source.Luma.ContainsAnyExcept(source.Luma[0]))
            {
                int mode = H264Intra16ModeSearch.Select(source, target, x, y, prediction[..256]);
                target.PredictMacroblock(0, x, y, mode, prediction[..256]);
                int chromaMode = H264ChromaModeSearch.Select(target, x, y, source.Chroma, prediction[256..]);
                H264Intra16Encoder.WriteCabac(syntax, source, target, address, x, y, quantizer, mode, chromaMode, prediction, coefficients);
                return;
            }
            syntax.WriteIntraType(address);
            int pattern = H264IntraLumaEncoder.Analyze(syntax, source.Luma, target, x, y, quantizer, coefficients[..256]);
            int selectedChroma = H264ChromaModeSearch.Select(target, x, y, source.Chroma, prediction[..128]);
            int chroma = H264ChromaEncoder.Analyze(source.Chroma, quantizer, prediction[..128], coefficients[256..]);
            syntax.WriteChromaMode(address, selectedChroma);
            syntax.WritePattern(address, pattern | (chroma << 4));
            if (pattern != 0 || chroma != 0) { syntax.WriteQuantizerDelta(0); }
            H264IntraLumaEncoder.TryWrite(syntax, target, x, y, pattern, coefficients[..256]);
            H264ChromaEncoder.TryWrite(syntax, target, x / 2, y / 2, quantizer, chroma, prediction[..128], coefficients[256..]);
        }

        private static void WriteInter<TAnalysis>(H264CabacEncodingSyntax syntax, H264SourceMacroblock source, H264Picture target,
            H264Picture reference, H264CabacPredictionWindow motion, int address, int x, int y, int quantizer,
            int searchRange, Span<byte> prediction, Span<int> coefficients, CancellationToken token, H264MotionReference? interpolation,
            H264RateDistortionAnalysis? analysis, H264SkipResidualBounds bounds, H264StationaryHistory? stationary, bool compareMotionRate,
            int lumaOffset)
            where TAnalysis : struct, IH264MacroblockAnalysisStrategy
        {
            motion.BeginMacroblock(address);
            (int skipX, int skipY) = x == 0 || y == 0 || stationary?.HasStationaryLeft(address) == true
                ? (0, 0) : H264MotionPrediction.Skip(target, x, y);
            int motionX;
            int motionY;
            int temporalDifference;
            int estimatedVectorBits = 0;
            bool skip = lumaOffset == 0 && (TAnalysis.Enabled
                ? analysis!.TrySkip(source, reference, address, x, y, skipX, skipY, searchRange, prediction, interpolation)
                : skipX == 0 && skipY == 0 && bounds.AllowsStationarySkip(source, reference, x, y));
            if (skip)
            {
                motionX = skipX;
                motionY = skipY;
                temporalDifference = 0;
            }
            else if (lumaOffset == 0)
            {
                (motionX, motionY, temporalDifference) = H264MotionSearch.SelectWithDifference<TAnalysis>(source, reference, target, x, y,
                    searchRange, prediction, token, interpolation, quantizer, out estimatedVectorBits);
            }
            else
            {
                (motionX, motionY, temporalDifference) = SearchWeighted(source, target, reference, x, y, searchRange,
                    prediction, token, interpolation, quantizer, lumaOffset, out estimatedVectorBits);
            }
            int pattern = 0;
            if (temporalDifference != 0)
            {
                if (!TAnalysis.Enabled && H264FastIntraCandidate.TryWrite(syntax, source, target, address, x, y,
                    quantizer, temporalDifference, compareMotionRate ? estimatedVectorBits : null, coefficients))
                {
                    stationary?.Record(address, source.Samples, false);
                    return;
                }
                pattern = H264InterEncoder.AnalyzeLuma(source.Luma, quantizer, prediction[..256], coefficients[..256]);
                pattern |= H264ChromaEncoder.Analyze(source.Chroma, quantizer, prediction[256..], coefficients[256..]) << 4;
            }
            if (TAnalysis.Enabled && analysis!.TryWrite(syntax, source, motion, x, y, pattern,
                motionX, motionY, prediction, coefficients)) { return; }
            bool skipped = pattern == 0 && motionX == skipX && motionY == skipY;
            bool stationarySkip = skipped && motionX == 0 && motionY == 0 && lumaOffset == 0;
            stationary?.Record(address, source.Samples, stationarySkip);
            syntax.WriteSkip(address, skipped);
            if (stationarySkip)
            {
                target.WriteStationarySkip(reference, x, y);
                return;
            }
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

        private static (int X, int Y, int Difference) SearchWeighted(H264SourceMacroblock source, H264Picture target,
            H264Picture reference, int x, int y, int searchRange, Span<byte> prediction, CancellationToken token,
            H264MotionReference? interpolation, int quantizer, int lumaOffset, out int estimatedVectorBits)
        {
            Span<byte> adjusted = stackalloc byte[H264SourceMacroblock.ByteCount];
            source.Samples.CopyTo(adjusted);
            H264LumaPredictionOffset.Apply(adjusted[..256], -lumaOffset);
            (int motionX, int motionY, _) = H264MotionSearch.SelectWithDifference<H264FastMacroblockAnalysis>(
                new H264SourceMacroblock(adjusted), reference, target, x, y, searchRange, prediction, token, interpolation,
                quantizer, out estimatedVectorBits);
            H264LumaPredictionOffset.Apply(prediction[..256], lumaOffset);
            return (motionX, motionY, H264SampleDifference.SumPackedMacroblock(source.Samples, prediction));
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
