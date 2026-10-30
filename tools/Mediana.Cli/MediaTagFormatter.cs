// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Mediana.Core.Metadata;

namespace Mediana.Cli
{
    internal static class MediaTagFormatter
    {
        public static string Format(MediaTag tag)
        {
            StringBuilder text = new("Tag ");
            text.Append(Quote(tag.Namespace)).Append('/').Append(Quote(tag.Name));
            if (tag.Description is not null)
            {
                text.Append(" description=").Append(Quote(tag.Description));
            }
            if (tag.Locale is uint locale)
            {
                text.Append(" locale=0x").Append(locale.ToString("x8", CultureInfo.InvariantCulture));
            }
            return text.Append(": [").AppendJoin(", ", tag.Values.Select(Quote)).Append(']').ToString();
        }

        private static string Quote(string value) =>
            "\"" + JsonEncodedText.Encode(value, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString() + "\"";
    }
}
