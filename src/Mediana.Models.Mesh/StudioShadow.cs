// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Models.Mesh
{
    internal class StudioShadow
    {
        private const int Radius = 2;
        private const int Taps = (2 * Radius) + 1;
        private const int AxisWeight = (Radius + 1) * (Radius + 1);
        private readonly RasterCamera _camera;
        private readonly MeshBuffer<float> _depths;
        private readonly int[] _offsets = new int[Taps];

        internal StudioShadow(RasterCamera camera, MeshBuffer<float> depths, float softness)
        {
            _camera = camera;
            _depths = depths;
            float step = MathF.Max(1, camera.Scale * softness);
            for (int tap = 0; tap < Taps; tap++)
            {
                _offsets[tap] = (int)MathF.Round((tap - Radius) * step);
            }
        }

        internal float Visibility(Vector3 point, Vector3 normal, float lightDot)
        {
            Vector3 projected = _camera.Project(point);
            int centerX = (int)MathF.Floor(projected.X);
            int centerY = (int)MathF.Floor(projected.Y);
            float bias = 0.0025f + (0.004f * (1 - MathF.Max(lightDot, 0)));
            float nl = MathF.Max(lightDot, 0.15f);
            float depthDx = Vector3.Dot(_camera.Unproject(projected.X + 1, projected.Y, projected.Z) - point, normal) / nl;
            float depthDy = Vector3.Dot(_camera.Unproject(projected.X, projected.Y + 1, projected.Z) - point, normal) / nl;
            Span<int> columns = stackalloc int[Taps];
            Span<float> horizontalDepths = stackalloc float[Taps];
            for (int tap = 0; tap < Taps; tap++)
            {
                int x = centerX + _offsets[tap];
                columns[tap] = x;
                horizontalDepths[tap] = projected.Z + (((x + 0.5f) - projected.X) * depthDx);
            }

            int visible = 0;
            int width = _camera.Width;
            ReadOnlySpan<float> depths = _depths.Span;
            for (int tapY = 0; tapY < Taps; tapY++)
            {
                int y = centerY + _offsets[tapY];
                int weightY = Radius + 1 - Math.Abs(tapY - Radius);
                if ((uint)y >= (uint)_camera.Height)
                {
                    visible += AxisWeight * weightY;
                    continue;
                }

                ReadOnlySpan<float> row = depths.Slice(y * width, width);
                float verticalDepth = ((y + 0.5f) - projected.Y) * depthDy;
                int visibleInRow = 0;
                for (int tapX = 0; tapX < Taps; tapX++)
                {
                    int x = columns[tapX];
                    if ((uint)x >= (uint)row.Length || horizontalDepths[tapX] + verticalDepth - bias <= row[x])
                    {
                        visibleInRow += Radius + 1 - Math.Abs(tapX - Radius);
                    }
                }
                visible += visibleInRow * weightY;
            }
            return (float)visible / (AxisWeight * AxisWeight);
        }
    }
}
