// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal readonly struct HdrRgbTransform : IRgbPixelTransform
    {
        private readonly RgbMatrix matrix;
        private readonly double[] transfer;
        private readonly bool hlg;
        private readonly double sourcePeak;
        private readonly double inverseTarget;
        private readonly double curveScale;
        private readonly double gamma;

        public HdrRgbTransform(ColorPrimaries primaries, TransferFunction function, HdrToneMappingOptions options)
        {
            options.Validate();
            matrix = RgbPrimariesTransform.ToSrgb(primaries);
            (transfer, hlg) = function switch
            {
                TransferFunction.Pq => (HdrTransfer.Pq, false),
                TransferFunction.Hlg when primaries == ColorPrimaries.Bt2020 => (HdrTransfer.Hlg, true),
                TransferFunction.Hlg => throw new NotSupportedException("HLG reference display conversion requires BT.2020 primaries."),
                TransferFunction.Unspecified or TransferFunction.Srgb or TransferFunction.Bt709 or TransferFunction.Gamma22 or
                    TransferFunction.Gamma28 or TransferFunction.Smpte240M or TransferFunction.Linear or TransferFunction.Log100 or
                    TransferFunction.Log316 or TransferFunction.Iec61966_2_4 or TransferFunction.Bt1361Extended or TransferFunction.Smpte428 =>
                    throw new ArgumentException("HDR tone mapping requires PQ or HLG input.", nameof(function)),
                _ => throw new ArgumentOutOfRangeException(nameof(function))
            };
            sourcePeak = options.SourcePeakLuminance;
            inverseTarget = 1 / options.TargetPeakLuminance;
            curveScale = 1 / Hable(sourcePeak * inverseTarget);
            gamma = sourcePeak >= 400 && sourcePeak <= 2000 ? 1.2 + 0.42 * Math.Log10(sourcePeak / 1000)
                : 1.2 * Math.Pow(1.111, Math.Log2(sourcePeak / 1000));
        }

        public void WritePixel(Span<byte> destination, ReadOnlySpan<byte> source) =>
            Write(destination, source[0] / 255.0, source[1] / 255.0, source[2] / 255.0, source[3]);

        public void Write(Span<byte> destination, double red, double green, double blue, byte alpha)
        {
            double r = HdrTransfer.Sample(transfer, red), g = HdrTransfer.Sample(transfer, green), b = HdrTransfer.Sample(transfer, blue);
            double displayScale = inverseTarget;
            if (hlg)
            {
                double luminance = 0.2627 * r + 0.6780 * g + 0.0593 * b;
                displayScale *= luminance > 0 ? sourcePeak * Math.Pow(luminance, gamma - 1) : 0;
            }
            (r, g, b) = matrix.Transform(r * displayScale, g * displayScale, b * displayScale);
            double signal = Math.Max(r, Math.Max(g, b));
            double scale = signal > 0 ? Hable(signal) * curveScale / signal : 0;
            destination[0] = SdrTransfer.EncodeSrgb(r * scale);
            destination[1] = SdrTransfer.EncodeSrgb(g * scale);
            destination[2] = SdrTransfer.EncodeSrgb(b * scale);
            destination[3] = alpha;
        }

        private static double Hable(double value)
        {
            if (value < 1)
            { return (value * (0.15 * value + 0.05) + 0.004) / (value * (0.15 * value + 0.50) + 0.06) - 0.02 / 0.30; }
            double inverse = 1 / value;
            return (0.15 + inverse * (0.05 + 0.004 * inverse)) / (0.15 + inverse * (0.50 + 0.06 * inverse)) - 0.02 / 0.30;
        }
    }
}
