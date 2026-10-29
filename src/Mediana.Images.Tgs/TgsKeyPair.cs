// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace Mediana.Images.Tgs
{
    internal readonly record struct TgsKeyPair(JsonElement Start, JsonElement End, JsonElement Key, double Progress);
}
