// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text.Json.Serialization;

namespace Mediana.DrawingBenchmarks
{
    [JsonSerializable(typeof(DrawingMeasurement))]
    internal partial class DrawingJsonContext : JsonSerializerContext
    {
    }
}
