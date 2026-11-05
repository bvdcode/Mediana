// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    internal class PdfExponentialFunction(double minimum, double maximum, double[] range,
        double[] start, double[] end, double exponent) : PdfFunction(minimum, maximum, range, start.Length)
    {
        internal override int EvaluationCost => 1;
        protected override void EvaluateCore(double input, Span<double> output)
        {
            double factor = Math.Pow(input, exponent);
            for (int i = 0; i < start.Length; i++) { output[i] = start[i] + (factor * (end[i] - start[i])); }
        }
    }
}
