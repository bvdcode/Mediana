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
    internal record TextCharacter(Rune Rune, TextSpan Span, bool Preserve);
}
