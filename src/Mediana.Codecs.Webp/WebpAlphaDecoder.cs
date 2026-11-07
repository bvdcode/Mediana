// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;

using Mediana.Core.Memory;

namespace Mediana.Codecs.Webp
{
    internal static class WebpAlphaDecoder
    {
        internal static void Decode(ReadOnlySpan<byte> encoded, Span<byte> pixels, int width, int height, MediaBufferWorkspace workspace, CancellationToken token)
        {
            if (encoded.Length == 0 || (encoded[0] & 0xc0) != 0 || ((encoded[0] >> 4) & 3) > 1 || (encoded[0] & 3) > 1)
            { throw new InvalidDataException("Invalid WebP alpha header."); }
            int compression = encoded[0] & 3, filter = (encoded[0] >> 2) & 3;
            Span<byte> alpha = workspace.Allocate(checked(width * height)).Span;
            if (compression == 0)
            {
                if (encoded.Length - 1 != alpha.Length) { throw new InvalidDataException("Invalid raw WebP alpha extent."); }
                encoded[1..].CopyTo(alpha);
            }
            else
            {
                Span<uint> temporary = MemoryMarshal.Cast<byte, uint>(workspace.Allocate(checked(width * height * 4)).Span);
                WebpBitReader input = new(encoded[1..]);
                WebpLosslessDecoder.DecodeImage(ref input, temporary, width, height, workspace, token);
                for (int index = 0; index < alpha.Length; index++) { alpha[index] = (byte)(temporary[index] >> 8); }
            }
            for (int y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x, prediction;
                    if (filter == 0) { prediction = 0; }
                    else if (y == 0) { prediction = x == 0 ? 0 : alpha[index - 1]; }
                    else if (x == 0) { prediction = alpha[index - width]; }
                    else
                    {
                        prediction = filter switch
                        {
                            1 => alpha[index - 1],
                            2 => alpha[index - width],
                            3 => Math.Clamp(alpha[index - 1] + alpha[index - width] - alpha[index - width - 1], 0, 255),
                            _ => throw new InvalidDataException("Invalid WebP alpha filter.")
                        };
                    }
                    alpha[index] = unchecked((byte)(alpha[index] + prediction)); pixels[index * 4 + 3] = alpha[index];
                }
            }
        }
    }
}
