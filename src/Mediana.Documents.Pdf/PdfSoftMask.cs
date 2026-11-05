// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Documents.Pdf
{
    internal record PdfSoftMask(GroupCommand Group, bool FromLuminosity, Rgba32 Background, byte[] Transfer)
    {
        internal PixelBuffer Render(PdfDrawingContext context)
        {
            PixelBuffer pixels = new(context.Width, context.Height, context.Allocator);
            bool completed = false;
            try
            {
                if (FromLuminosity) { pixels.Clear(Background); }
                Group.Draw(new RasterCanvas(pixels), context);
                Span<byte> data = pixels.Pixels.Span;
                for (int y = 0; y < context.Height; y++)
                {
                    context.Token.ThrowIfCancellationRequested();
                    Span<byte> row = data.Slice(y * context.Width * 4, context.Width * 4);
                    for (int x = 0; x < row.Length; x += 4)
                    {
                        byte alpha = FromLuminosity ? Luminosity(row[x], row[x + 1], row[x + 2]) : row[x + 3];
                        row[x] = row[x + 1] = row[x + 2] = 255;
                        row[x + 3] = Transfer[alpha];
                    }
                }
                completed = true;
                return pixels;
            }
            finally
            {
                if (!completed) { pixels.Dispose(); }
            }
        }

        internal static byte Luminosity(byte red, byte green, byte blue) =>
            (byte)Math.Clamp((int)Math.Round((.3 * red) + (.59 * green) + (.11 * blue)), 0, 255);
    }
}
