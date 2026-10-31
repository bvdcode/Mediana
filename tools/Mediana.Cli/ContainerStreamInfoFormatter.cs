// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Containers;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;

namespace Mediana.Cli
{
    internal static class ContainerStreamInfoFormatter
    {
        public static async Task WriteAsync(ContainerStreamInfo track, TextWriter output, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (track.Language is string language)
            {
                await output.WriteLineAsync($"  Language: {MediaTagFormatter.Quote(language)}.");
            }
            if (track.DeclaredDuration is MediaDuration declared)
            {
                await output.WriteLineAsync($"  Declared track duration: {MediaDurationFormatter.Seconds(declared)} seconds.");
            }
            if (track.PacketIndex is ContainerPacketIndexInfo index)
            {
                await output.WriteLineAsync($"  Indexed packets: {index.PacketCount}; encoded payload: {index.PayloadBytes} bytes.");
                MediaDuration duration = index.DecodeDuration;
                await output.WriteLineAsync($"  Indexed decode duration: {MediaDurationFormatter.Seconds(duration)} seconds.");
                if (duration.Value > 0)
                {
                    double rate = (double)index.PayloadBytes * 8 * duration.TimeBase.Denominator / duration.Value / duration.TimeBase.Numerator;
                    await output.WriteLineAsync($"  Mean indexed payload bitrate: {rate.ToString("0.###", CultureInfo.InvariantCulture)} bits/s.");
                }
            }
            if (track.Tags is IReadOnlyList<MediaTag> tags)
            {
                await output.WriteLineAsync($"  Stream text tags: {tags.Count}.");
                foreach (MediaTag tag in tags)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await output.WriteLineAsync("  " + MediaTagFormatter.Format(tag));
                }
            }
        }
    }
}
