// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.Intrinsics;

namespace Mediana.Models.Mesh
{
    internal class ScreenSpaceOcclusion
    {
        internal const float WorldRadius = 0.18f;
        internal const float MinimumDistance = 0.0001f;
        internal const float HorizonBias = 0.06f;
        private const int Steps = 3;
        private static readonly Vector2[] Directions =
        [
            new(1, 0), new(0.707107f, 0.707107f), new(0, 1), new(-0.707107f, 0.707107f),
            new(-1, 0), new(-0.707107f, -0.707107f), new(0, -1), new(0.707107f, -0.707107f)
        ];

        private readonly RasterCamera _camera;
        private readonly OcclusionSamples[] _samples = Vector128.IsHardwareAccelerated
            ? new OcclusionSamples[Directions.Length / Vector128<float>.Count * Steps] : [];
        private readonly Vector2[] _scalarOffsets = Vector128.IsHardwareAccelerated ? [] : new Vector2[Directions.Length * Steps];

        internal ScreenSpaceOcclusion(RasterCamera camera)
        {
            _camera = camera;
            float radius = MathF.Max(2, camera.Scale * WorldRadius);
            int tap = 0;
            for (int first = 0; first < Directions.Length; first += Vector128<float>.Count)
            {
                ReadOnlySpan<Vector2> directions = Directions.AsSpan(first, Vector128<float>.Count);
                for (int step = 0; step < Steps; step++)
                {
                    float distance = (radius * (step + 1)) / Steps;
                    if (Vector128.IsHardwareAccelerated)
                    {
                        _samples[tap++] = new OcclusionSamples(directions, distance);
                    }
                    else
                    {
                        for (int lane = 0; lane < directions.Length; lane++)
                        {
                            _scalarOffsets[((first + lane) * Steps) + step] = directions[lane] * distance;
                        }
                    }
                }
            }
        }

        internal float Evaluate(int x, int y, Vector3 point, Vector3 normal, ReadOnlySpan<float> depths)
        {
            float occlusion = Vector128.IsHardwareAccelerated
                ? SumHorizons(x, y, point, normal, depths)
                : SumScalarHorizons(x, y, point, normal, depths);
            return Math.Clamp(1 - (occlusion * (2.5f / Directions.Length)), 0.25f, 1);
        }

        private float SumHorizons(int x, int y, Vector3 point, Vector3 normal, ReadOnlySpan<float> depths)
        {
            float occlusion = 0;
            for (int group = 0; group < _samples.Length; group += Steps)
            {
                Vector128<float> horizons = Vector128<float>.Zero;
                for (int step = 0; step < Steps; step++)
                {
                    horizons = Vector128.Max(horizons, _samples[group + step].Evaluate(_camera, x, y, point, normal, depths));
                }
                // Keep the scalar direction order when adding the four horizons.
                for (int lane = 0; lane < Vector128<float>.Count; lane++) { occlusion += horizons[lane]; }
            }
            return occlusion;
        }

        private float SumScalarHorizons(int x, int y, Vector3 point, Vector3 normal, ReadOnlySpan<float> depths)
        {
            float occlusion = 0;
            for (int start = 0; start < _scalarOffsets.Length; start += Steps)
            {
                float horizon = 0;
                for (int step = 0; step < Steps; step++)
                {
                    Vector2 offset = _scalarOffsets[start + step];
                    int sx = (int)MathF.Round(x + offset.X);
                    int sy = (int)MathF.Round(y + offset.Y);
                    if ((uint)sx >= (uint)_camera.Width || (uint)sy >= (uint)_camera.Height)
                    {
                        continue;
                    }

                    float depth = depths[(sy * _camera.Width) + sx];
                    if (!float.IsFinite(depth))
                    {
                        continue;
                    }

                    Vector3 delta = _camera.Unproject(sx + 0.5f, sy + 0.5f, depth) - point;
                    float length = delta.Length();
                    if (length < MinimumDistance || length >= WorldRadius)
                    {
                        continue;
                    }

                    float contribution = MathF.Max((Vector3.Dot(normal, delta) / length) - HorizonBias, 0) * (1 - (length / WorldRadius));
                    horizon = MathF.Max(horizon, contribution);
                }

                occlusion += horizon;
            }

            return occlusion;
        }
    }
}
