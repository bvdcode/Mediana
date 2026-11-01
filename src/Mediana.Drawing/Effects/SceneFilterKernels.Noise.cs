// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
namespace Mediana.Drawing
{
    internal static partial class SceneFilterKernels
    {
        internal static PixelBuffer Turbulence(Vector2 frequency, int octaves, long seed, bool fractal, bool stitch, PixelBuffer source, Matrix3x2 matrix, (float X, float Y, float Width, float Height) region, bool linear, SceneRenderContext context)
        {
            if (frequency.X < 0 || frequency.Y < 0 || octaves < 0 || octaves > 12)
            {
                throw new InvalidDataException("Invalid Drawing turbulence frequency/octaves (maximum 12).");
            }

            SceneNoise? noise = new SceneNoise(seed);
            if (stitch)
            {
                frequency.X = StitchFrequency(frequency.X, region.Width);
                frequency.Y = StitchFrequency(frequency.Y, region.Height);
            }

            if (!Matrix3x2.Invert(matrix, out Matrix3x2 inverse))
            {
                return context.CreateBuffer(source.Width, source.Height);
            }

            SceneNoiseOctave[] levels = new SceneNoiseOctave[octaves];
            float amplitude = 1;
            for (int octave = 0; octave < octaves; octave++)
            {
                levels[octave] = new SceneNoiseOctave(frequency, amplitude,
                    stitch ? (int)MathF.Round(region.Width * frequency.X) : 0, stitch ? (int)MathF.Round(region.Height * frequency.Y) : 0,
                    (int)MathF.Floor(region.X * frequency.X), (int)MathF.Floor(region.Y * frequency.Y));
                frequency *= 2;
                amplitude *= .5f;
            }

            return ScenePixels.Map(source, (x, y) =>
            {
                Vector2 point = Vector2.Transform(new Vector2(x + .5f, y + .5f), inverse);
                Vector4 sum = Vector4.Zero;
                foreach (SceneNoiseOctave level in levels)
                {
                    Vector4 sample = noise.Sample(point.X * level.Frequency.X, point.Y * level.Frequency.Y, level.Width, level.Height, level.OriginX, level.OriginY);
                    sum += (fractal ? sample : Vector4.Abs(sample)) * level.Amplitude;
                }

                return fractal ? (sum + Vector4.One) / 2 : sum;
            }, linear, context, false, parallel: true);
        }
        private static float StitchFrequency(float value, float size)
        {
            if (value == 0 || size <= 0)
            {
                return value;
            }

            float low = MathF.Floor(size * value) / size, high = MathF.Ceiling(size * value) / size;
            return low > 0 && value / low < high / value ? low : high;
        }
    }
}
