// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Models.Mesh
{
    internal readonly struct TriangleNormals(Vector3 first, Vector3 second, Vector3 third, Vector3 face, Vector3 view)
    {
        internal Vector3 Interpolate(float wa, float wb, float wc)
        {
            Vector3 normal = (first * wa) + (second * wb) + (third * wc);
            if (normal.LengthSquared() < 0.000001f) { normal = face; }

            normal = Vector3.Normalize(normal);
            return Vector3.Dot(normal, view) < 0 ? -normal : normal;
        }
    }
}
