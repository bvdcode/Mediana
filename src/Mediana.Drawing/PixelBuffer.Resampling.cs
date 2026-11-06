// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Threading;

namespace Mediana.Drawing
{
    public partial class PixelBuffer
    {
        /// <summary>Downsamples by an integer factor using premultiplied-alpha averaging.</summary>
        public PixelBuffer Downsample(int factor, CancellationToken cancellationToken = default)
        {
            if (factor < 1 || factor > 16 || Width % factor != 0 || Height % factor != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(factor));
            }

            PixelBuffer output = new(Width / factor, Height / factor);
            bool completed = false;
            try
            {
                DownsampleTo(output, factor, cancellationToken);
                completed = true;
                return output;
            }
            finally
            {
                if (!completed)
                {
                    output.Dispose();
                }
            }
        }

        /// <summary>Downsamples into an existing surface using premultiplied-alpha averaging.</summary>
        public void DownsampleTo(PixelBuffer output, int factor, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(output);
            if (factor < 1 || factor > 16 || Width % factor != 0 || Height % factor != 0
                || output.Width != Width / factor || output.Height != Height / factor)
            {
                throw new ArgumentOutOfRangeException(nameof(factor));
            }
            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> source = Pixels.Span;
            Span<byte> destination = output.Pixels.Span;
            int stride = Width * 4;
            int samples = factor * factor;
            for (int y = 0; y < output.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < output.Width; x++)
                {
                    int r = 0, g = 0, b = 0, alpha = 0;
                    int input = (y * factor * stride) + x * factor * 4;
                    for (int dy = 0; dy < factor; dy++)
                    {
                        ReadOnlySpan<byte> row = source.Slice(input + dy * stride, factor * 4);
                        for (int offset = 0; offset < row.Length; offset += 4)
                        {
                            int a = row[offset + 3];
                            alpha += a;
                            r += row[offset] * a;
                            g += row[offset + 1] * a;
                            b += row[offset + 2] * a;
                        }
                    }
                    Span<byte> pixel = destination.Slice((y * output.Width + x) * 4, 4);
                    if (alpha == 0) { pixel.Clear(); }
                    else
                    {
                        pixel[0] = (byte)(r / alpha);
                        pixel[1] = (byte)(g / alpha);
                        pixel[2] = (byte)(b / alpha);
                        pixel[3] = (byte)(alpha / samples);
                    }
                }
            }
        }

    }
}
