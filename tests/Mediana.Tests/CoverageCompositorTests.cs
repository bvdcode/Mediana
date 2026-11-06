// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Tests
{
    public class CoverageCompositorTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FractionalSpansMatchScalarForEveryAlphaPairAndUnalignedTails(bool mixedBackdrop)
        {
            const int count = 37;
            byte[] expected = new byte[count * 4], actual = new byte[count * 4];
            float[] coverage = new float[count];
            Random random = new(5194);
            for (int sourceAlpha = 0; sourceAlpha < 256; sourceAlpha++)
            {
                for (int backdropAlpha = 0; backdropAlpha < 256; backdropAlpha++)
                {
                    random.NextBytes(actual);
                    for (int i = 0; i < count; i++)
                    {
                        actual[i * 4 + 3] = (byte)(mixedBackdrop ? (backdropAlpha + i * 53) & 255 : backdropAlpha);
                        coverage[i] = i switch
                        {
                            0 => -.1f,
                            1 => 0,
                            2 => 1,
                            3 => 1.1f,
                            4 => .5f,
                            5 => .5f / 255,
                            6 => 254.5f / 255,
                            _ => random.NextSingle()
                        };
                    }
                    actual.CopyTo(expected, 0);
                    Rgba32 color = new((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)sourceAlpha);
                    for (int i = 0; i < count; i++)
                    {
                        byte alpha = (byte)MathF.Round(sourceAlpha * Math.Clamp(coverage[i], 0, 1));
                        PixelCompositor.Blend(expected, i * 4, color with { A = alpha }, ColorBlendMode.Normal);
                    }
                    PixelCompositor.BlendCoverageSpan(actual, coverage, color);
                    Assert.Equal(expected, actual);
                }
            }
        }
    }
}
