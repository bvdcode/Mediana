// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Cli
{
    internal static class MediaProbeCommand
    {
        public static async Task<int> RunAsync(string input, TextWriter output, CancellationToken cancellationToken)
        {
            MediaEngine engine = ContainerEngineFactory.CreateBuilder().Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            MediaInfo info = await engine.ProbeAsync(source, cancellationToken: cancellationToken);
            await output.WriteLineAsync($"Container: {info.Container.FormatId}; {info.Streams.Count} streams.");
            if (info.Container.DeclaredDuration is MediaDuration duration)
            {
                double seconds = (double)duration.Value * duration.TimeBase.Numerator / duration.TimeBase.Denominator;
                await output.WriteLineAsync($"Declared duration: {seconds.ToString("0.#########", CultureInfo.InvariantCulture)} seconds.");
            }
            else
            {
                await output.WriteLineAsync("Declared duration: unknown.");
            }
            foreach (EncodedStreamInfo stream in info.Streams)
            {
                await output.WriteLineAsync($"Stream {stream.StreamIndex}: {stream.Kind}; {stream.CodecId}; time base {stream.TimeBase.Numerator}/{stream.TimeBase.Denominator}.");
            }
            if (info.Container.Tags is IReadOnlyList<MediaTag> tags)
            {
                await output.WriteLineAsync($"Header text tags: {tags.Count}.");
                foreach (MediaTag tag in tags)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await output.WriteLineAsync(MediaTagFormatter.Format(tag));
                }
            }
            else
            {
                await output.WriteLineAsync("Header text tags: unavailable.");
            }
            return 0;
        }
    }
}
