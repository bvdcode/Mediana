// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Xml.Linq;

namespace Mediana.Images.Svg
{
    internal class TextSpan
    {
        internal required XElement Element;
        internal required Dictionary<string, string> Style;
        internal required TextSpan? Parent;
        internal float Size;
        internal Matrix3x2 Transform;
        internal int Index;
        internal float[] X = Array.Empty<float>(), Y = Array.Empty<float>(), Dx = Array.Empty<float>(), Dy = Array.Empty<float>(), Rotate = Array.Empty<float>();
    }
}
