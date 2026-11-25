// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using System.Numerics;

namespace Mediana.Models.Mesh
{
    internal static class MeshAdjacency
    {
        private const int MaximumNeighbors = 64;

        internal static void CreateNormals(ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> indices,
            ReadOnlySpan<Vector3> faceNormals, ReadOnlySpan<Vector3> faceAreas, Span<Vector3> normals,
            float cosine, MediaBufferAllocator allocator, CancellationToken token)
        {
            int capacity = checked((int)BitOperations.RoundUpToPowerOf2(checked((uint)indices.Length * 2)));
            using MeshBuffer<int> headBuffer = new(capacity, allocator);
            using MeshBuffer<int> nextBuffer = new(indices.Length, allocator);
            Span<int> heads = headBuffer.Span;
            Span<int> next = nextBuffer.Span;
            heads.Fill(-1);
            for (int corner = indices.Length - 1; corner >= 0; corner--)
            {
                if ((corner & 4095) == 0) { token.ThrowIfCancellationRequested(); }
                Vector3 normal = faceNormals[corner / 3];
                normals[corner] = normal;
                if (normal == Vector3.Zero) { continue; }
                int slot = FindSlot(positions[indices[corner]], positions, indices, heads);
                next[corner] = heads[slot];
                heads[slot] = corner;
            }

            Span<int> neighbors = stackalloc int[MaximumNeighbors];
            for (int slot = 0; slot < heads.Length; slot++)
            {
                if ((slot & 4095) == 0) { token.ThrowIfCancellationRequested(); }
                if (heads[slot] < 0) { continue; }
                int count = 0;
                int corner = heads[slot];
                while (corner >= 0 && count < neighbors.Length)
                {
                    neighbors[count++] = corner;
                    corner = next[corner];
                }
                if (corner < 0)
                {
                    Smooth(neighbors[..count], normals, faceNormals, faceAreas, cosine);
                }
            }
        }

        private static void Smooth(ReadOnlySpan<int> corners, Span<Vector3> normals,
            ReadOnlySpan<Vector3> faceNormals, ReadOnlySpan<Vector3> faceAreas, float cosine)
        {
            foreach (int corner in corners)
            {
                Vector3 normal = faceNormals[corner / 3];
                Vector3 sum = Vector3.Zero;
                foreach (int adjacent in corners)
                {
                    int face = adjacent / 3;
                    if (Vector3.Dot(normal, faceNormals[face]) >= cosine - 0.000001f) { sum += faceAreas[face]; }
                }
                if (sum.LengthSquared() > 1e-24f) { normals[corner] = Vector3.Normalize(sum); }
            }
        }

        private static int FindSlot(Vector3 position, ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> indices, ReadOnlySpan<int> heads)
        {
            int mask = heads.Length - 1;
            int slot = position.GetHashCode() & mask;
            while (heads[slot] >= 0 && positions[indices[heads[slot]]] != position)
            {
                slot = (slot + 1) & mask;
            }
            return slot;
        }
    }
}
