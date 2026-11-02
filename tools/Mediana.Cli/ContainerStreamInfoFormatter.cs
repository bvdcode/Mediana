// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Containers;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Core.Video;

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
            await MediaDateTimeFormatter.WriteAsync(track.Dates, output, "  ", cancellationToken);
            if (track.AverageFrameRate is FrameRate frameRate)
            {
                await output.WriteLineAsync(FormattableString.Invariant($"  Average frame rate: {frameRate.Numerator}/{frameRate.Denominator} ({frameRate.ToDouble():0.###} fps)."));
            }
            if (track.DeclaredDuration is MediaDuration declared)
            {
                await output.WriteLineAsync($"  Declared track duration: {MediaDurationFormatter.Seconds(declared)} seconds.");
            }
            if (track.PacketIndex is ContainerPacketIndexInfo index)
            {
                await output.WriteLineAsync($"  Indexed packets: {index.PacketCount}; encoded payload: {index.PayloadBytes} bytes.");
                if (index.DecodeDuration is MediaDuration duration)
                {
                    await output.WriteLineAsync($"  Indexed decode duration: {MediaDurationFormatter.Seconds(duration)} seconds.");
                }
                if (index.AveragePayloadBitRate is double rate)
                {
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
