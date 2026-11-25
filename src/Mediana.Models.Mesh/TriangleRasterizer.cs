// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using System;
using System.Numerics;
using System.Threading;

namespace Mediana.Models.Mesh
{
    internal static class TriangleRasterizer
    {
        internal static void Draw(PreparedMesh mesh, RasterCamera camera, Span<float> depths, Span<Vector3> normals, MediaBufferAllocator allocator, CancellationToken token)
        {
            Span<Vector3> positions = mesh.Positions;
            Span<Vector3> cornerNormals = mesh.Normals;
            depths.Fill(float.PositiveInfinity);
            using MeshBuffer<Vector3> projection = new(positions.Length, allocator);
            Span<Vector3> projected = projection.Span;
            for (int index = 0; index < projected.Length; index++)
            {
                if ((index & 4095) == 0) { token.ThrowIfCancellationRequested(); }
                projected[index] = camera.Project(positions[index]);
            }

            for (int corner = 0; corner < mesh.Indices.Length; corner += 3)
            {
                token.ThrowIfCancellationRequested();
                if (cornerNormals[corner] == Vector3.Zero)
                {
                    continue;
                }

                Vector3 a = projected[mesh.Indices[corner]];
                Vector3 b = projected[mesh.Indices[corner + 1]];
                Vector3 c = projected[mesh.Indices[corner + 2]];
                float area = Edge(a, b, c.X, c.Y);
                if (MathF.Abs(area) < 0.000001f)
                {
                    continue;
                }

                int left = Math.Clamp((int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))), 0, camera.Width - 1);
                int right = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))), 0, camera.Width - 1);
                int top = Math.Clamp((int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))), 0, camera.Height - 1);
                int bottom = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))), 0, camera.Height - 1);
                float inverseArea = 1 / area;
                TriangleNormals surface = default;
                if (!normals.IsEmpty)
                {
                    Vector3 origin = positions[mesh.Indices[corner]];
                    Vector3 face = Vector3.Cross(positions[mesh.Indices[corner + 1]] - origin, positions[mesh.Indices[corner + 2]] - origin);
                    surface = new(cornerNormals[corner], cornerNormals[corner + 1], cornerNormals[corner + 2], face, camera.ViewDirection);
                }
                for (int y = top; y <= bottom; y++)
                {
                    token.ThrowIfCancellationRequested();
                    int offset = (y * camera.Width) + left;
                    int width = right - left + 1;
                    TriangleDepthRow row = new(a, b, c, y + 0.5f, inverseArea);
                    row.Draw(left, depths.Slice(offset, width), normals.IsEmpty ? default : normals.Slice(offset, width), in surface, token);
                }
            }
        }

        private static float Edge(Vector3 a, Vector3 b, float x, float y) =>
            ((b.X - a.X) * (y - a.Y)) - ((b.Y - a.Y) * (x - a.X));
    }
}
