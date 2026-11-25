// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using System.Numerics;

namespace Mediana.Models.Mesh
{
    internal class PreparedMesh : IDisposable
    {
        private readonly MeshBuffer<Vector3> _positions;
        private MeshBuffer<Vector3>? _normals;
        internal Span<Vector3> Positions => _positions.Span;
        internal Span<Vector3> Normals => _normals!.Span;
        internal int[] Indices { get; }

        internal PreparedMesh(TriangleMesh source, MeshRenderOptions options, MediaBufferAllocator allocator, CancellationToken token)
        {
            Indices = source.IndexData;
            _positions = new(source.VertexData.Length, allocator);
            bool completed = false;
            try
            {
                Quaternion rotation = Quaternion.Normalize(options.ModelRotation);
                NormalizePositions(source, rotation, token);
                using MeshBuffer<Vector3> faceNormals = new(source.TriangleCount, allocator);
                using MeshBuffer<Vector3> faceAreas = new(source.TriangleCount, allocator);
                CalculateFaces(faceNormals.Span, faceAreas.Span, token);
                _normals = new(Indices.Length, allocator);
                if (options.SmoothNormals)
                {
                    float cosine = MathF.Cos((options.CreaseAngle * MathF.PI) / 180);
                    MeshAdjacency.CreateNormals(Positions, Indices, faceNormals.Span, faceAreas.Span, Normals, cosine, allocator, token);
                }
                else
                {
                    CopyFaceNormals(faceNormals.Span, Normals, token);
                }
                if (options.UseSourceNormals && source.NormalData.Length > 0)
                {
                    ApplySourceNormals(source.NormalData, rotation, token);
                }
                completed = true;
            }
            finally
            {
                if (!completed) { Dispose(); }
            }
        }

        private void NormalizePositions(TriangleMesh source, Quaternion rotation, CancellationToken token)
        {
            Vector3 minimum = new(float.PositiveInfinity);
            Vector3 maximum = new(float.NegativeInfinity);
            foreach (int index in source.IndexData)
            {
                if ((index & 4095) == 0) { token.ThrowIfCancellationRequested(); }
                minimum = Vector3.Min(minimum, source.VertexData[index]);
                maximum = Vector3.Max(maximum, source.VertexData[index]);
            }
            double centerX = ((double)minimum.X + maximum.X) * 0.5;
            double centerY = ((double)minimum.Y + maximum.Y) * 0.5;
            double centerZ = ((double)minimum.Z + maximum.Z) * 0.5;
            double extent = Math.Max((double)maximum.X - minimum.X,
                Math.Max((double)maximum.Y - minimum.Y, (double)maximum.Z - minimum.Z));
            if (extent == 0) { throw new ArgumentException("The mesh has no spatial extent.", nameof(source)); }
            Span<Vector3> positions = Positions;
            for (int index = 0; index < positions.Length; index++)
            {
                if ((index & 4095) == 0) { token.ThrowIfCancellationRequested(); }
                Vector3 vertex = source.VertexData[index];
                Vector3 normalized = new((float)(((vertex.X - centerX) * 2) / extent),
                    (float)(((vertex.Y - centerY) * 2) / extent), (float)(((vertex.Z - centerZ) * 2) / extent));
                positions[index] = Vector3.Transform(normalized, rotation);
            }
        }

        private void CalculateFaces(Span<Vector3> faceNormals, Span<Vector3> faceAreas, CancellationToken token)
        {
            ReadOnlySpan<Vector3> positions = Positions;
            bool hasSurface = false;
            for (int face = 0; face < faceNormals.Length; face++)
            {
                token.ThrowIfCancellationRequested();
                int corner = face * 3;
                Vector3 a = positions[Indices[corner]], b = positions[Indices[corner + 1]], c = positions[Indices[corner + 2]];
                Vector3 area = Vector3.Cross(b - a, c - a);
                if (area.LengthSquared() < 1e-24f) { continue; }
                hasSurface = true;
                faceAreas[face] = area;
                faceNormals[face] = Vector3.Normalize(area);
            }
            if (!hasSurface) { throw new ArgumentException("The mesh contains no nondegenerate triangles."); }
        }

        private static void CopyFaceNormals(ReadOnlySpan<Vector3> faceNormals, Span<Vector3> normals, CancellationToken token)
        {
            for (int corner = 0; corner < normals.Length; corner++)
            {
                if ((corner & 4095) == 0) { token.ThrowIfCancellationRequested(); }
                normals[corner] = faceNormals[corner / 3];
            }
        }

        private void ApplySourceNormals(ReadOnlySpan<Vector3> sourceNormals, Quaternion rotation, CancellationToken token)
        {
            Span<Vector3> normals = Normals;
            for (int corner = 0; corner < normals.Length; corner++)
            {
                if ((corner & 4095) == 0) { token.ThrowIfCancellationRequested(); }
                if (sourceNormals[corner] != Vector3.Zero && normals[corner] != Vector3.Zero)
                {
                    normals[corner] = Vector3.Transform(GeometryMath.Normalize(sourceNormals[corner]), rotation);
                }
            }
        }

        public void Dispose()
        {
            _normals?.Dispose();
            _positions.Dispose();
        }
    }
}
