// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Models.Mesh
{
    internal class ScreenSpaceOcclusion
    {
        private const float WorldRadius = 0.18f;
        private const int Steps = 3;
        private static readonly Vector2[] Directions =
        [
            new(1, 0), new(0.707107f, 0.707107f), new(0, 1), new(-0.707107f, 0.707107f),
            new(-1, 0), new(-0.707107f, -0.707107f), new(0, -1), new(0.707107f, -0.707107f)
        ];

        private readonly RasterCamera _camera;
        private readonly Vector2[] _offsets = new Vector2[Directions.Length * Steps];

        internal ScreenSpaceOcclusion(RasterCamera camera)
        {
            _camera = camera;
            float radius = MathF.Max(2, camera.Scale * WorldRadius);
            for (int direction = 0; direction < Directions.Length; direction++)
            {
                for (int step = 0; step < Steps; step++)
                {
                    float distance = (radius * (step + 1)) / Steps;
                    _offsets[(direction * Steps) + step] = Directions[direction] * distance;
                }
            }
        }

        internal float Evaluate(int x, int y, Vector3 point, Vector3 normal, ReadOnlySpan<float> depths)
        {
            float occlusion = 0;
            for (int start = 0; start < _offsets.Length; start += Steps)
            {
                float horizon = 0;
                for (int step = 0; step < Steps; step++)
                {
                    Vector2 offset = _offsets[start + step];
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
                    if (length < 0.0001f || length >= WorldRadius)
                    {
                        continue;
                    }

                    float contribution = MathF.Max((Vector3.Dot(normal, delta) / length) - 0.06f, 0) * (1 - (length / WorldRadius));
                    horizon = MathF.Max(horizon, contribution);
                }

                occlusion += horizon;
            }

            return Math.Clamp(1 - (occlusion * (2.5f / Directions.Length)), 0.25f, 1);
        }
    }
}
