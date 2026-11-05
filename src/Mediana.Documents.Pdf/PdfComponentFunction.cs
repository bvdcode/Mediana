// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    internal class PdfComponentFunction(PdfFunction[] components) : PdfFunction(double.MinValue, double.MaxValue, [], components.Length)
    {
        internal override int EvaluationCost { get; } = checked(1 + components.Sum(function => function.EvaluationCost));
        protected override void EvaluateCore(double input, Span<double> output)
        {
            for (int i = 0; i < components.Length; i++) { components[i].Evaluate(input, output.Slice(i, 1)); }
        }
    }
}
