// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal class H264RateDistortionAnalysis(H264CabacMacroblockState state, H264Picture target, int quantizer)
    {
        private static readonly double[] Lambdas = Enumerable.Range(0, 52)
            .Select(qp => 0.85 * Math.Pow(2, (qp - 12) / 3.0)).ToArray();
        private readonly H264CabacCostSyntax costSyntax = new(state, target, quantizer);

        public bool TrySkip(H264SourceMacroblock source, H264Picture reference, int address, int x, int y,
            int motionX, int motionY, int searchRange, Span<byte> prediction, H264MotionReference? interpolation) =>
            H264QuantizedSkipAnalysis.TryAnalyze(state, source, reference, address, x, y,
                motionX, motionY, quantizer, searchRange, prediction, interpolation);

        public bool TryWrite(H264CabacEncodingSyntax syntax, H264SourceMacroblock source,
            H264CabacPredictionWindow motion, int x, int y, int interPattern, int motionX, int motionY,
            ReadOnlySpan<byte> interPrediction, ReadOnlySpan<int> interCoefficients)
        {
            if ((interPattern & 15) != 15) { return false; }
            Span<byte> prediction = stackalloc byte[H264SourceMacroblock.ByteCount];
            Span<int> coefficients = stackalloc int[H264Intra16Encoder.CoefficientCount];
            Span<byte> interPixels = stackalloc byte[H264SourceMacroblock.ByteCount];
            Span<byte> intraPixels = stackalloc byte[H264SourceMacroblock.ByteCount];
            int mode = H264Intra16ModeSearch.Select(source, target, x, y, prediction[..256]);
            int pattern = H264Intra16Encoder.Analyze(source, target, x, y, quantizer, mode, prediction, coefficients);
            long interError = H264MacroblockReconstruction.Prepare(source, quantizer, interPattern,
                interPrediction, interCoefficients, false, interPixels);
            long intraError = H264MacroblockReconstruction.Prepare(source, quantizer, pattern,
                prediction, coefficients, true, intraPixels);
            H264PreparedMacroblock inter = new(-1, interPattern, interCoefficients, interPixels, interError);
            H264PreparedMacroblock intra = new(mode, pattern, coefficients, intraPixels, intraError);
            H264MacroblockMotion vector = H264MacroblockMotion.Create(target, motion, x, y, motionX, motionY);
            Choose(syntax, motion, x, y, inter, intra, vector);
            return true;
        }

        private void Choose(H264CabacEncodingSyntax syntax, H264CabacPredictionWindow motion,
            int x, int y, H264PreparedMacroblock inter, H264PreparedMacroblock intra, H264MacroblockMotion vector)
        {
            bool firstIntra = intra.Distortion < inter.Distortion;
            H264PreparedMacroblock first = firstIntra ? intra : inter;
            H264PreparedMacroblock other = firstIntra ? inter : intra;
            Span<H264CabacContext> preContexts = stackalloc H264CabacContext[syntax.ContextCount];
            syntax.CopyContexts(preContexts);
            int previousDelta = syntax.PreviousDelta;
            Span<byte> preCounts = stackalloc byte[24];
            H264CabacMacroblockSnapshot preState = H264CabacMacroblockSnapshot.Capture(state, target, x, y, preCounts);
            H264CabacEncoderCheckpoint engine = syntax.CaptureEngine();
            H264CabacRateSample initial = syntax.RateSample;
            Span<short> savedMotion = stackalloc short[32];
            H264MacroblockMotion.Copy(motion, x, y, savedMotion, false);
            H264PreparedMacroblockWriter.Write(syntax, target, motion, x, y, first, vector);
            double firstRate = H264CabacRate.Difference(initial, syntax.RateSample);
            double lambda = Lambdas[quantizer];
            double firstCost = first.Distortion + lambda * firstRate;
            double otherCost = double.PositiveInfinity;
            if (firstIntra ? other.Distortion <= firstCost : other.Distortion < firstCost)
            {
                Span<byte> acceptedCounts = stackalloc byte[24];
                H264CabacMacroblockSnapshot acceptedState = H264CabacMacroblockSnapshot.Capture(state, target, x, y, acceptedCounts);
                try
                {
                    preState.Restore(state, target, x, y, preCounts);
                    costSyntax.Reset(engine.Range, preContexts, previousDelta);
                    double rate = costSyntax.Estimate(x, y, other, vector, new(other.Distortion, lambda, firstCost, firstIntra));
                    otherCost = other.Distortion + lambda * rate;
                }
                finally { acceptedState.Restore(state, target, x, y, acceptedCounts); }
            }
            bool chooseOther = firstIntra ? otherCost <= firstCost : otherCost < firstCost;
            if (!chooseOther) { return; }
            syntax.RestoreEngine(engine);
            syntax.RestoreContexts(preContexts);
            syntax.PreviousDelta = previousDelta;
            preState.Restore(state, target, x, y, preCounts);
            H264MacroblockMotion.Copy(motion, x, y, savedMotion, true);
            H264PreparedMacroblockWriter.Write(syntax, target, motion, x, y, other, vector);
        }
    }
}
