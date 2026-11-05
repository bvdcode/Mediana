// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System.Numerics;

namespace Mediana.Models.Mesh
{
    internal readonly record struct ComponentReference(ObjectKey Key, Matrix4x4 Transform);
}
