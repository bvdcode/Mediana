// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text;
using Mediana.Core.Metadata;

namespace Mediana.Cli
{
    internal static class MediaDateTimeFormatter
    {
        public static async Task WriteAsync(IReadOnlyList<MediaDateTimeTag>? dates, TextWriter output,
            string indent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (dates is null)
            {
                return;
            }
            foreach (MediaDateTimeTag date in dates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await output.WriteLineAsync(indent + Format(date));
            }
        }

        public static string Format(MediaDateTimeTag date)
        {
            StringBuilder text = new("Date ");
            text.Append(MediaTagFormatter.Quote(date.Namespace)).Append('/').Append(MediaTagFormatter.Quote(date.Name));
            if (date.SourcePath is string source)
            {
                text.Append(" source=").Append(MediaTagFormatter.Quote(source));
            }
            text.Append(": ").Append(date.Value.ToString("O", CultureInfo.InvariantCulture));
            switch (date.Value.Kind)
            {
                case DateTimeKind.Utc:
                    return text.ToString();
                case DateTimeKind.Unspecified:
                    return text.Append(" (time zone unspecified)").ToString();
                case DateTimeKind.Local:
                default:
                    throw new InvalidOperationException("Media date metadata has an invalid time-zone basis.");
            }
        }
    }
}
