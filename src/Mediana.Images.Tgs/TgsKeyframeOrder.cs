// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace Mediana.Images.Tgs
{
    internal static class TgsKeyframeOrder
    {
        internal static JsonElement Restore(JsonElement root, CancellationToken token)
        {
            ArrayBufferWriter<byte> buffer = new();
            using (Utf8JsonWriter writer = new(buffer))
            {
                Write(root, writer, token);
            }

            using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
            return document.RootElement.Clone();
        }

        private static void Write(JsonElement element, Utf8JsonWriter writer, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        writer.WritePropertyName(property.Name);
                        Write(property.Value, writer, token);
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    bool keys = element.GetArrayLength() > 0 && TgsJson.Get(element[0], "ty").ValueKind == JsonValueKind.Undefined
                        && TgsJson.Get(element[0], "t").ValueKind == JsonValueKind.Number;
                    IEnumerable<JsonElement> values = element.EnumerateArray();
                    if (keys)
                    {
                        values = values.OrderBy(key => TgsJson.Number(key, "t"));
                    }
                    foreach (JsonElement child in values)
                    {
                        Write(child, writer, token);
                    }
                    writer.WriteEndArray();
                    break;
                default:
                    element.WriteTo(writer);
                    break;
            }
        }
    }
}
