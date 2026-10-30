// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.Threading;

namespace Mediana.Drawing
{
    internal record SceneClip(SceneClipShape[] Shapes, bool ObjectBox);
}
