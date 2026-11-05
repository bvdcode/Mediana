// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    internal partial class PdfInterpreter
    {
        private void ChargeOperations(int count)
        {
            token.ThrowIfCancellationRequested();
            if (count > storage.Budget.Options.MaximumOperations - _operations)
            { throw new InvalidDataException("PDF page exceeds its operation limit."); }
            _operations += count;
        }
        private double[] Numbers(PdfValue value, int count = -1)
        {
            value = Resolve(value);
            if (value.Kind != PdfValueKind.Array || count >= 0 && value.Items.Length != count)
            { throw new InvalidDataException("Invalid PDF numeric array."); }
            double[] values = new double[value.Items.Length];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = Resolve(value.Items[i]).Numeric();
                if (!double.IsFinite(values[i])) { throw new InvalidDataException("Non-finite PDF numeric array."); }
            }
            return values;
        }
        private PdfFunction ReadFunction(PdfValue value, int depth = 0)
        {
            ChargeOperations(1);
            if (depth >= storage.Budget.Options.MaximumDepth) { throw new InvalidDataException("Excessive PDF function nesting."); }
            value = Resolve(value);
            if (value.Kind == PdfValueKind.Array)
            {
                if (value.Items.Length is < 1 or > 4) { throw new InvalidDataException("Invalid PDF component functions."); }
                PdfFunction[] components = value.Items.Select(item => ReadFunction(item, depth + 1)).ToArray();
                if (components.Any(function => function.OutputCount != 1)) { throw new InvalidDataException("PDF component functions require one output."); }
                return new PdfComponentFunction(components);
            }
            if (value.Kind != PdfValueKind.Dictionary) { throw new InvalidDataException("PDF function requires a dictionary."); }
            double[] domain = Numbers(Get(value, "Domain"), 2);
            if (domain[0] >= domain[1]) { throw new InvalidDataException("Invalid PDF function domain."); }
            double[] range = value.Get("Range").Kind == PdfValueKind.Null ? [] : Numbers(Get(value, "Range"));
            if (range.Length > 8 || range.Length % 2 != 0) { throw new InvalidDataException("Invalid PDF function range."); }
            for (int i = 0; i < range.Length; i += 2)
            { if (range[i] > range[i + 1]) { throw new InvalidDataException("Reversed PDF function range."); } }
            PdfFunction result;
            switch (Get(value, "FunctionType").Integer())
            {
                case 2:
                    double[] start = value.Get("C0").Kind == PdfValueKind.Null ? [0] : Numbers(Get(value, "C0"));
                    double[] end = value.Get("C1").Kind == PdfValueKind.Null ? [1] : Numbers(Get(value, "C1"));
                    double exponent = Get(value, "N").Numeric();
                    if (start.Length is < 1 or > 4 || end.Length != start.Length || !double.IsFinite(exponent))
                    { throw new InvalidDataException("Invalid PDF exponential function."); }
                    result = new PdfExponentialFunction(domain[0], domain[1], range, start, end, exponent);
                    break;
                case 3:
                    PdfValue children = Get(value, "Functions");
                    if (children.Kind != PdfValueKind.Array || children.Items.Length == 0)
                    { throw new InvalidDataException("PDF stitching function requires child functions."); }
                    PdfFunction[] functions = children.Items.Select(item => ReadFunction(item, depth + 1)).ToArray();
                    if (functions.Any(function => function.OutputCount != functions[0].OutputCount))
                    { throw new InvalidDataException("Inconsistent PDF stitching function outputs."); }
                    double[] bounds = Numbers(Get(value, "Bounds"), functions.Length - 1);
                    double previous = domain[0];
                    foreach (double bound in bounds)
                    {
                        if (bound <= previous || bound >= domain[1]) { throw new InvalidDataException("Invalid PDF stitching bounds."); }
                        previous = bound;
                    }
                    result = new PdfStitchingFunction(domain[0], domain[1], range, functions, bounds, Numbers(Get(value, "Encode"), functions.Length * 2));
                    break;
                default: throw new NotSupportedException("Unsupported PDF function type.");
            }
            if (range.Length != 0 && range.Length != result.OutputCount * 2)
            { throw new InvalidDataException("PDF function range does not match its outputs."); }
            return result;
        }
    }
}
