// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System.Numerics;

namespace Mediana.Models.Mesh
{
    internal record ObjectResource(ThreeMfObject Info, Vector3[] Vertices, int[] Indices, ComponentReference[] Components);
}
