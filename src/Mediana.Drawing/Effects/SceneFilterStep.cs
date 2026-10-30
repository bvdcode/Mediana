// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.Linq;
namespace Mediana.Drawing
{
    internal record SceneFilterStep(Func<SceneFilterInput, PixelBuffer> Render)
    {
        internal string Input { get; init; } = "";
        internal string? SecondInput { get; init; }
        internal string Result { get; init; } = "";
        internal bool? Linear { get; init; }
        internal Func<(float X, float Y, float Width, float Height), (float X, float Y, float Width, float Height)>? Region { get; init; }
        internal IReadOnlyList<string> AdditionalInputs { get; init; } = Array.Empty<string>();
        internal IEnumerable<string> Inputs => new[] { Input, SecondInput ?? "" }.Concat(AdditionalInputs).Where(name => name.Length > 0);
    }
}
