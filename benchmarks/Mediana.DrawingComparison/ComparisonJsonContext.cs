// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text.Json.Serialization;

namespace Mediana.DrawingComparison
{
    [JsonSerializable(typeof(ComparisonMeasurement))]
    internal partial class ComparisonJsonContext : JsonSerializerContext { }
}
