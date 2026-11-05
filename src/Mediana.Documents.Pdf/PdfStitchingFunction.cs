// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    internal class PdfStitchingFunction(double minimum, double maximum, double[] range,
        PdfFunction[] functions, double[] bounds, double[] encode)
        : PdfFunction(minimum, maximum, range, functions[0].OutputCount)
    {
        internal override int EvaluationCost { get; } = checked(1 + functions.Max(function => function.EvaluationCost));
        protected override void EvaluateCore(double input, Span<double> output)
        {
            int segment = Array.BinarySearch(bounds, input);
            segment = segment >= 0 ? segment + 1 : ~segment;
            double low = segment == 0 ? Minimum : bounds[segment - 1];
            double high = segment == bounds.Length ? Maximum : bounds[segment];
            double factor = (input - low) / (high - low);
            functions[segment].Evaluate(encode[segment * 2] + (factor * (encode[(segment * 2) + 1] - encode[segment * 2])), output);
        }
    }
}
