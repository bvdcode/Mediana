// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    internal abstract class PdfFunction(double minimum, double maximum, double[] range, int outputCount)
    {
        protected double Minimum { get; } = minimum;
        protected double Maximum { get; } = maximum;
        internal int OutputCount { get; } = outputCount;
        internal abstract int EvaluationCost { get; }
        internal void Evaluate(double input, Span<double> output)
        {
            EvaluateCore(Math.Clamp(input, Minimum, Maximum), output);
            for (int i = 0; i < OutputCount; i++)
            {
                if (!double.IsFinite(output[i])) { throw new InvalidDataException("Non-finite PDF function result."); }
                if (range.Length != 0) { output[i] = Math.Clamp(output[i], range[i * 2], range[(i * 2) + 1]); }
            }
        }
        protected abstract void EvaluateCore(double input, Span<double> output);
    }
}
