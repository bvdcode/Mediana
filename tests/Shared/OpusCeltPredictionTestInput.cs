// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;

namespace Mediana.Tests
{
    internal static class OpusCeltPredictionTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "celt-prediction.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSCLP1"u8)) { throw new InvalidDataException("The CELT prediction reference header is invalid."); }
            Span<float> history = stackalloc float[4096], correlation = stackalloc float[25], coefficients = stackalloc float[24];
            Span<float> residual = stackalloc float[512], signal = stackalloc float[536];
            int count = reader.ReadInt32();
            Span<float> window = stackalloc float[120];
            for (int at = 0; at < window.Length; at++) { window[at] = reader.ReadSingle(); }
            for (int test = 0; test < count; test++)
            {
                int channels = reader.ReadInt32();
                for (int at = 0; at < channels * 2048; at++) { history[at] = reader.ReadSingle(); }
                int expectedPitch = reader.ReadInt32(), pitch = CeltPitchSearch.Find(history, channels, 2048);
                if (pitch != expectedPitch) { throw new InvalidDataException($"CELT pitch {test} differs: {pitch} / {expectedPitch}."); }
                for (int channel = 0; channel < channels; channel++)
                {
                    ReadOnlySpan<float> plane = history.Slice(channel * 2048, 2048);
                    CeltLpcAnalysis.Correlate(plane[1024..], correlation, window);
                    VerifyValues(reader, correlation, test, "autocorrelation");
                    correlation[0] *= 1.0001f;
                    for (int lag = 1; lag < correlation.Length; lag++) { correlation[lag] -= correlation[lag] * (.008f * .008f) * lag * lag; }
                    CeltLpcAnalysis.Predict(correlation, coefficients);
                    VerifyValues(reader, coefficients, test, "LPC");
                    CeltLpcAnalysis.Residual(plane[(2048 - 512 - 24)..], coefficients, residual);
                    VerifyValues(reader, residual, test, "residual");
                    plane[^24..].CopyTo(signal); residual.CopyTo(signal[24..]);
                    CeltLpcAnalysis.Synthesize(signal, coefficients, 512);
                    VerifyValues(reader, signal[24..], test, "synthesis");
                }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length) { throw new InvalidDataException("The CELT prediction reference has trailing bytes."); }
            return count;
        }

        private static void VerifyValues(BinaryReader reader, ReadOnlySpan<float> actual, int test, string stage)
        {
            for (int at = 0; at < actual.Length; at++)
            {
                float expected = reader.ReadSingle();
                if (!float.IsFinite(actual[at]) || Math.Abs(actual[at] - expected) > 1e-5f * Math.Max(1, Math.Abs(expected)))
                {
                    throw new InvalidDataException($"CELT {stage} {test}, value {at} differs: {actual[at]} / {expected}.");
                }
            }
        }
    }
}
