// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text.Json.Serialization;

namespace Mediana.VectorBenchmarks
{
    [JsonSerializable(typeof(VectorMeasurement))]
    internal partial class VectorJsonContext : JsonSerializerContext
    {
    }
}
