// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using System.Numerics;

namespace Mediana.Models.Mesh
{
    internal class MeshAdjacency : IDisposable
    {
        private readonly MeshBuffer<int> _heads;
        private MeshBuffer<int>? _next;
        private readonly PreparedMesh _mesh;
        private readonly int _mask;

        internal MeshAdjacency(PreparedMesh mesh, ReadOnlySpan<Vector3> faceNormals, MediaBufferAllocator allocator, CancellationToken token)
        {
            _mesh = mesh;
            int capacity = checked((int)BitOperations.RoundUpToPowerOf2(checked((uint)mesh.Indices.Length * 2)));
            _mask = capacity - 1;
            _heads = new(capacity, allocator);
            bool completed = false;
            try
            {
                _next = new(mesh.Indices.Length, allocator);
                _heads.Span.Fill(-1);
                for (int corner = mesh.Indices.Length - 1; corner >= 0; corner--)
                {
                    if ((corner & 4095) == 0) { token.ThrowIfCancellationRequested(); }
                    if (faceNormals[corner / 3] == Vector3.Zero) { continue; }
                    int slot = Slot(mesh.Positions[mesh.Indices[corner]]);
                    _next.Span[corner] = _heads.Span[slot];
                    _heads.Span[slot] = corner;
                }
                completed = true;
            }
            finally
            {
                if (!completed) { Dispose(); }
            }
        }

        internal Vector3 Smooth(int corner, ReadOnlySpan<Vector3> faceNormals, ReadOnlySpan<Vector3> faceAreas, float cosine)
        {
            Vector3 normal = faceNormals[corner / 3];
            int first = _heads.Span[Slot(_mesh.Positions[_mesh.Indices[corner]])];
            Vector3 sum = Vector3.Zero;
            int count = 0;
            for (int current = first; current >= 0; current = _next!.Span[current])
            {
                if (++count > 64) { return normal; }
                int neighbor = current / 3;
                if (Vector3.Dot(normal, faceNormals[neighbor]) >= cosine - 0.000001f) { sum += faceAreas[neighbor]; }
            }
            return sum.LengthSquared() > 1e-24f ? Vector3.Normalize(sum) : normal;
        }

        private int Slot(Vector3 position)
        {
            int slot = position.GetHashCode() & _mask;
            ReadOnlySpan<int> heads = _heads.Span;
            ReadOnlySpan<Vector3> positions = _mesh.Positions;
            while (true)
            {
                int head = heads[slot];
                if (head < 0 || positions[_mesh.Indices[head]] == position) { return slot; }
                slot = (slot + 1) & _mask;
            }
        }

        public void Dispose()
        {
            _next?.Dispose();
            _heads.Dispose();
        }
    }
}
