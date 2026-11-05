// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Core.Metadata;

namespace Mediana.Cli
{
    internal static class MediaMetadataFormatter
    {
        internal static async Task WriteAsync(MediaMetadata metadata, TextWriter output, CancellationToken cancellationToken)
        {
            Stack<(MediaMetadata Metadata, int Depth)> pending = new();
            pending.Push((metadata, 0));
            while (pending.TryPop(out (MediaMetadata Metadata, int Depth) current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (current.Metadata.Properties.Count != 0 || current.Metadata.Children.Count != 0)
                {
                    string indent = new(' ', checked(current.Depth * 2));
                    await output.WriteLineAsync($"{indent}Metadata {MediaTagFormatter.Quote(current.Metadata.Scope)}:");
                    foreach (MediaProperty property in current.Metadata.Properties)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string values = string.Join(", ", property.Values.Select(Format));
                        await output.WriteLineAsync($"{indent}  {MediaTagFormatter.Quote(property.Namespace)}/{MediaTagFormatter.Quote(property.Name)}: {values}");
                    }
                }
                for (int index = current.Metadata.Children.Count - 1; index >= 0; index--)
                {
                    pending.Push((current.Metadata.Children[index], checked(current.Depth + 1)));
                }
            }
        }

        private static string Format(MediaPropertyValue value) => value.Kind switch
        {
            MediaPropertyValueKind.Text => MediaTagFormatter.Quote(value.AsText()),
            MediaPropertyValueKind.Integer => value.AsInteger().ToString(CultureInfo.InvariantCulture),
            MediaPropertyValueKind.UnsignedInteger => value.AsUnsignedInteger().ToString(CultureInfo.InvariantCulture),
            MediaPropertyValueKind.Number => value.AsNumber().ToString("R", CultureInfo.InvariantCulture),
            MediaPropertyValueKind.Boolean => value.AsBoolean() ? "true" : "false",
            MediaPropertyValueKind.Rational => string.Create(CultureInfo.InvariantCulture, $"{value.AsRational().Numerator}/{value.AsRational().Denominator}"),
            MediaPropertyValueKind.Binary => string.Create(CultureInfo.InvariantCulture, $"{value.AsBytes().Length} binary bytes"),
            MediaPropertyValueKind.Undefined => throw new InvalidOperationException("Undefined metadata value."),
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }
}
