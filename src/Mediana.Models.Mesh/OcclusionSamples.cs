// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.Intrinsics;

namespace Mediana.Models.Mesh
{
    internal readonly struct OcclusionSamples(ReadOnlySpan<Vector2> directions, float distance)
    {
        private readonly Vector128<float> _x = Vector128.Create(directions[0].X, directions[1].X, directions[2].X, directions[3].X) * distance;
        private readonly Vector128<float> _y = Vector128.Create(directions[0].Y, directions[1].Y, directions[2].Y, directions[3].Y) * distance;

        internal Vector128<float> Evaluate(RasterCamera camera, int x, int y, Vector3 point, Vector3 normal, ReadOnlySpan<float> depths)
        {
            Vector128<float> sx = Vector128.Round(Vector128.Create((float)x) + _x);
            Vector128<float> sy = Vector128.Round(Vector128.Create((float)y) + _y);
            Vector128<int> ix = Vector128.ConvertToInt32(sx);
            Vector128<int> iy = Vector128.ConvertToInt32(sy);
            Vector128<int> inside = Vector128.LessThan(ix.AsUInt32(), Vector128.Create((uint)camera.Width)).AsInt32()
                & Vector128.LessThan(iy.AsUInt32(), Vector128.Create((uint)camera.Height)).AsInt32();
            // Out-of-bounds lanes read index zero and contribute zero to the horizon.
            Vector128<int> index = ((iy * camera.Width) + ix) & inside;
            Vector128<float> depth = Vector128.Create(depths[index[0]], depths[index[1]], depths[index[2]], depths[index[3]]);
            camera.Unproject(sx + Vector128.Create(0.5f), sy + Vector128.Create(0.5f), depth,
                out Vector128<float> wx, out Vector128<float> wy, out Vector128<float> wz);
            Vector128<float> dx = wx - Vector128.Create(point.X);
            Vector128<float> dy = wy - Vector128.Create(point.Y);
            Vector128<float> dz = wz - Vector128.Create(point.Z);
            Vector128<float> length = Vector128.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            Vector128<float> valid = inside.AsSingle() & Vector128.GreaterThanOrEqual(length, Vector128.Create(ScreenSpaceOcclusion.MinimumDistance))
                & Vector128.LessThan(length, Vector128.Create(ScreenSpaceOcclusion.WorldRadius));
            Vector128<float> dot = (dx * normal.X) + (dy * normal.Y) + (dz * normal.Z);
            Vector128<float> angular = Vector128.Max((dot / length) - Vector128.Create(ScreenSpaceOcclusion.HorizonBias), Vector128<float>.Zero);
            return (angular * (Vector128<float>.One - (length / ScreenSpaceOcclusion.WorldRadius))) & valid;
        }
    }
}
