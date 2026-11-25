// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.Intrinsics;

namespace Mediana.Models.Mesh
{
    internal readonly struct TriangleDepthRow(Vector3 a, Vector3 b, Vector3 c, float y, float inverseArea)
    {
        private const float MinimumWeight = -0.000001f;
        private readonly float _firstRow = (c.X - b.X) * (y - b.Y);
        private readonly float _firstSlope = c.Y - b.Y;
        private readonly float _secondRow = (a.X - c.X) * (y - c.Y);
        private readonly float _secondSlope = a.Y - c.Y;

        internal void Draw(int left, Span<float> depths, Span<Vector3> normals, in TriangleNormals surface, CancellationToken token)
        {
            int i = 0;
            if (Vector128.IsHardwareAccelerated && depths.Length >= Vector128<float>.Count)
            {
                i = DrawBlocks(left, depths, normals, in surface, token);
            }
            for (; i < depths.Length; i++)
            {
                if ((i & 255) == 0) { token.ThrowIfCancellationRequested(); }
                float x = (left + i) + 0.5f;
                float wa = (_firstRow - (_firstSlope * (x - b.X))) * inverseArea;
                float wb = (_secondRow - (_secondSlope * (x - c.X))) * inverseArea;
                float wc = 1 - wa - wb;
                if (wa < MinimumWeight || wb < MinimumWeight || wc < MinimumWeight) { continue; }
                float depth = (wa * a.Z) + (wb * b.Z) + (wc * c.Z);
                if (depth >= depths[i]) { continue; }
                depths[i] = depth;
                if (!normals.IsEmpty) { normals[i] = surface.Interpolate(wa, wb, wc); }
            }
        }

        private int DrawBlocks(int left, Span<float> depths, Span<Vector3> normals, in TriangleNormals surface, CancellationToken token)
        {
            int i = 0;
            ref float first = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(depths);
            Vector128<float> limit = Vector128.Create(MinimumWeight);
            for (; i <= depths.Length - Vector128<float>.Count; i += Vector128<float>.Count)
            {
                if ((i & 255) == 0) { token.ThrowIfCancellationRequested(); }
                Vector128<float> x = (Vector128.Create((float)(left + i)) + Vector128<float>.Indices) + Vector128.Create(0.5f);
                Vector128<float> wa = (Vector128.Create(_firstRow) - ((x - Vector128.Create(b.X)) * _firstSlope)) * inverseArea;
                Vector128<float> wb = (Vector128.Create(_secondRow) - ((x - Vector128.Create(c.X)) * _secondSlope)) * inverseArea;
                Vector128<float> wc = Vector128<float>.One - wa - wb;
                Vector128<float> outside = Vector128.LessThan(wa, limit) | Vector128.LessThan(wb, limit) | Vector128.LessThan(wc, limit);
                Vector128<float> depth = (wa * a.Z) + (wb * b.Z) + (wc * c.Z);
                Vector128<float> prior = Vector128.LoadUnsafe(ref first, (nuint)i);
                Vector128<float> keep = outside | Vector128.GreaterThanOrEqual(depth, prior);
                uint accepted = (~keep).ExtractMostSignificantBits();
                if (accepted == 0) { continue; }
                Vector128.ConditionalSelect(keep, prior, depth).StoreUnsafe(ref first, (nuint)i);
                if (normals.IsEmpty) { continue; }
                while (accepted != 0)
                {
                    int lane = BitOperations.TrailingZeroCount(accepted);
                    normals[i + lane] = surface.Interpolate(wa[lane], wb[lane], wc[lane]);
                    accepted &= accepted - 1;
                }
            }
            return i;
        }
    }
}
