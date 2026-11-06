// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Tests
{
    public class PixelCompositorSpanTests
    {
        [Fact]
        public void ConstantSourceSpansMatchScalarForEveryAlphaPairAndUnalignedTails()
        {
            Random random = new(713);
            byte[] actual = new byte[4 * 37], expected = new byte[actual.Length];
            for (int alpha = 0; alpha < 256; alpha++)
            {
                for (int background = 0; background < 256; background++)
                {
                    random.NextBytes(actual);
                    for (int i = 3; i < actual.Length; i += 4) { actual[i] = (byte)background; }
                    actual.CopyTo(expected, 0);
                    Rgba32 source = new((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)alpha);
                    PixelCompositor.BlendSpan(actual, source);
                    for (int i = 0; i < expected.Length; i += 4) { PixelCompositor.Blend(expected, i, source, ColorBlendMode.Normal); }
                    Assert.Equal(expected, actual);
                }
            }
        }
    }
}
