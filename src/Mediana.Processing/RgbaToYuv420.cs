// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    /// <summary>Converts opaque straight RGBA8 to YUV420P8, averaging each chroma cell before rounding.
    /// Uses fixed-point matrix coefficients and one output allocation. Borrows input and allocator.
    /// RGB primaries/transfer are preserved; the caller explicitly selects the output matrix and range.</summary>
    public class RgbaToYuv420
    {
        private const int Precision = 16;
        private const int Unit = 1 << Precision;
        private readonly VideoFormat inputFormat;
        private readonly MediaBufferAllocator allocator;
        private readonly int redY, greenY, blueY, offsetY;
        private readonly int redCb, greenCb, blueCb, redCr, greenCr, blueCr;

        public RgbaToYuv420(VideoFormat inputFormat, RgbVideoConversionOptions options, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(inputFormat);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(allocator);
            VideoColorInfo color = inputFormat.GetColorInterpretation();
            if (inputFormat.PixelFormat != VideoPixelFormat.Rgba32 || color.Matrix != MatrixCoefficients.Rgb ||
                color.Range != ColorRange.Full || color.ChromaLocation != ChromaLocation.Unspecified)
            { throw new NotSupportedException("RGB video conversion requires full-range straight RGBA8 with an RGB matrix."); }
            YuvRgbMatrix matrix = YuvRgbMatrix.Create(options.Matrix, options.Range);
            this.inputFormat = inputFormat;
            this.allocator = allocator;
            OutputFormat = new(inputFormat.Width, inputFormat.Height, VideoPixelFormat.Yuv420P8,
                color with { Matrix = options.Matrix, Range = options.Range, ChromaLocation = ChromaLocation.Center },
                inputFormat.SampleAspectRatio);
            redY = Coefficient(matrix.RedWeight * matrix.LumaScale / 255);
            blueY = Coefficient(matrix.BlueWeight * matrix.LumaScale / 255);
            greenY = Coefficient(matrix.LumaScale / 255) - redY - blueY;
            offsetY = Coefficient(matrix.LumaOffset);
            redCb = Coefficient(-matrix.RedWeight * matrix.ChromaScale / (510 * (1 - matrix.BlueWeight)));
            blueCb = Coefficient(matrix.ChromaScale / 510);
            greenCb = -redCb - blueCb;
            redCr = Coefficient(matrix.ChromaScale / 510);
            blueCr = Coefficient(-matrix.BlueWeight * matrix.ChromaScale / (510 * (1 - matrix.RedWeight)));
            greenCr = -redCr - blueCr;
        }

        public VideoFormat OutputFormat { get; }

        public VideoFrame Apply(VideoFrame input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            cancellationToken.ThrowIfCancellationRequested();
            if (input.Format.Width != inputFormat.Width || input.Format.Height != inputFormat.Height ||
                input.Format.PixelFormat != inputFormat.PixelFormat || input.Format.SampleAspectRatio != inputFormat.SampleAspectRatio ||
                input.Format.GetColorInterpretation() != inputFormat.GetColorInterpretation())
            { throw new NotSupportedException("The input frame differs from the configured RGB video conversion."); }
            PlaneLayout[] planes = OutputFormat.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)planes[^1].EndOffset));
            Span<byte> output = writable.Memory.Span;
            for (int top = 0; top < input.Format.Height; top += 2)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int rows = Math.Min(2, input.Format.Height - top);
                for (int left = 0; left < input.Format.Width; left += 2)
                {
                    if ((left & 1023) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                    int columns = Math.Min(2, input.Format.Width - left);
                    int red = 0, green = 0, blue = 0;
                    for (int y = 0; y < rows; y++)
                    {
                        ReadOnlySpan<byte> source = input.GetRow(0, top + y);
                        Span<byte> luma = output.Slice(planes[0].Offset + (top + y) * planes[0].Stride + left, columns);
                        for (int x = 0; x < columns; x++)
                        {
                            ReadOnlySpan<byte> pixel = source.Slice((left + x) * 4, 4);
                            if (pixel[3] != 255) { throw new NotSupportedException("RGB video conversion requires opaque pixels; composite transparency explicitly before conversion."); }
                            red += pixel[0]; green += pixel[1]; blue += pixel[2];
                            luma[x] = Quantize(pixel[0] * redY + pixel[1] * greenY + pixel[2] * blueY + offsetY, Precision);
                        }
                    }
                    int shift = Precision + ((rows * columns) switch
                    {
                        1 => 0,
                        2 => 1,
                        4 => 2,
                        _ => throw new InvalidOperationException("The chroma averaging cell is invalid.")
                    });
                    int offset = 128 << shift;
                    int cell = top / 2 * planes[1].Stride + left / 2;
                    output[planes[1].Offset + cell] = Quantize(red * redCb + green * greenCb + blue * blueCb + offset, shift);
                    output[planes[2].Offset + cell] = Quantize(red * redCr + green * greenCr + blue * blueCr + offset, shift);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            return new(OutputFormat, buffer, planes, input.Timestamp, input.Duration);
        }

        private static int Coefficient(double value) => checked((int)Math.Round(value * Unit, MidpointRounding.AwayFromZero));
        private static byte Quantize(int value, int shift) => (byte)Math.Clamp((value + (1 << (shift - 1))) >> shift, 0, 255);
    }
}
