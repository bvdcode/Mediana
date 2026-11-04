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
        internal static JsonElement Restore(JsonElement root, TgsReaderOptions options, CancellationToken token)
        {
            ArrayBufferWriter<byte> buffer = new();
            using (Utf8JsonWriter writer = new(buffer))
            {
                Write(root, writer, options, token);
            }

            using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
            return document.RootElement.Clone();
        }

        private static void Write(JsonElement element, Utf8JsonWriter writer, TgsReaderOptions options, CancellationToken token, bool validate = true, bool effect = false)
        {
            token.ThrowIfCancellationRequested();
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    validate = validate && !(effect && TgsEffectInfo.IsIgnored(element, options));
                    writer.WriteStartObject();
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        writer.WritePropertyName(property.Name);
                        Write(property.Value, writer, options, token, validate, property.Name == "ef");
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    bool keys = validate && element.GetArrayLength() > 0 && TgsJson.Get(element[0], "ty").ValueKind == JsonValueKind.Undefined
                        && TgsJson.Get(element[0], "t").ValueKind == JsonValueKind.Number;
                    IEnumerable<JsonElement> values = element.EnumerateArray();
                    if (keys)
                    {
                        values = values.OrderBy(key => TgsJson.Number(key, "t"));
                    }
                    foreach (JsonElement child in values)
                    {
                        Write(child, writer, options, token, validate, effect);
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
