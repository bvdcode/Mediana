// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.DrawingComparison
{
    internal readonly record struct PathCommand(PathVerb Verb, Vector2 First = default, Vector2 Second = default, Vector2 Third = default);
}
