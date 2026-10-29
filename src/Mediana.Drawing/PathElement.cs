// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Mediana.Drawing
{
    internal readonly record struct PathElement(int Kind, Vector2 First, Vector2 Second, Vector2 Third);
}
