// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Core.Timing;

namespace Mediana.Cli
{
    internal static class MediaDurationFormatter
    {
        public static string Seconds(MediaDuration duration) =>
            ((double)duration.Value * duration.TimeBase.Numerator / duration.TimeBase.Denominator)
                .ToString("0.#########", CultureInfo.InvariantCulture);
    }
}
