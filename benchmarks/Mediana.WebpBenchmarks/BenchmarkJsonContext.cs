// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text.Json.Serialization;

namespace Mediana.WebpBenchmarks
{
    [JsonSerializable(typeof(BenchmarkResult))]
    internal partial class BenchmarkJsonContext : JsonSerializerContext;
}
