// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Drawing
{
    internal static class SceneChannelLookup
    {
        internal static bool TryGetSourceChannels(ReadOnlySpan<float> matrix, Span<int> channels)
        {
            for (int row = 0; row < 4; row++)
            {
                int source = -1;
                for (int column = 0; column < 4; column++)
                {
                    if (matrix[row * 5 + column] == 0) { continue; }
                    if (source >= 0) { return false; }
                    source = column;
                }
                channels[row] = Math.Max(0, source);
            }
            return true;
        }

        internal static PixelBuffer Apply(PixelBuffer input, ReadOnlySpan<byte> lookup, ReadOnlySpan<int> channels,
            SceneRenderContext context, bool overwrite)
        {
            PixelBuffer output = overwrite ? input : context.CreateBuffer(input.Width, input.Height);
            ReadOnlySpan<byte> source = input.Pixels.Span;
            Span<byte> destination = output.Pixels.Span;
            int r = channels[0], g = channels[1], b = channels[2], a = channels[3];
            for (int y = 0; y < input.Height; y++)
            {
                context.Token.ThrowIfCancellationRequested();
                int end = (y + 1) * input.Width * 4;
                for (int i = y * input.Width * 4; i < end; i += 4)
                {
                    byte red = lookup[source[i + r] * 4], green = lookup[source[i + g] * 4 + 1],
                        blue = lookup[source[i + b] * 4 + 2], alpha = lookup[source[i + a] * 4 + 3];
                    destination[i] = red; destination[i + 1] = green; destination[i + 2] = blue; destination[i + 3] = alpha;
                }
            }
            return output;
        }
    }
}
