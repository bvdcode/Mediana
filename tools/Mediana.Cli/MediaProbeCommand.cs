// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Containers;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Cli
{
    internal static class MediaProbeCommand
    {
        public static async Task<int> RunAsync(string input, TextWriter output, CancellationToken cancellationToken)
        {
            MediaEngine engine = MetadataEngineFactory.Create();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            MediaInfo info = await engine.ProbeAsync(source, CommandImageResources.ProbeOptions, cancellationToken);
            if (source.Length is long length)
            {
                await output.WriteLineAsync($"Encoded size: {length} bytes.");
            }
            if (info.Image is ImageInfo image)
            {
                await ImageInfoFormatter.WriteAsync(image, output);
            }
            else
            {
                await WriteContainerAsync(info, output, cancellationToken);
            }
            await MediaMetadataFormatter.WriteAsync(info.Metadata, output, cancellationToken);
            return 0;
        }

        private static async Task WriteContainerAsync(MediaInfo info, TextWriter output, CancellationToken cancellationToken)
        {
            ContainerInfo container = info.Container ?? throw new InvalidOperationException("Media metadata has no representation.");
            await output.WriteLineAsync($"Container: {container.FormatId}; {info.Streams.Count} streams.");
            if (container.DeclaredDuration is MediaDuration duration)
            {
                await output.WriteLineAsync($"Declared duration: {MediaDurationFormatter.Seconds(duration)} seconds.");
            }
            else
            {
                await output.WriteLineAsync("Declared duration: unknown.");
            }
            await MediaDateTimeFormatter.WriteAsync(container.Dates, output, "", cancellationToken);
            foreach (EncodedStreamInfo stream in info.Streams)
            {
                await output.WriteLineAsync($"Stream {stream.StreamIndex}: {stream.Kind}; {stream.CodecId}; time base {stream.TimeBase.Numerator}/{stream.TimeBase.Denominator}.");
                if (container.StreamInfo is not null && container.StreamInfo.TryGetValue(stream.StreamIndex, out ContainerStreamInfo? track))
                {
                    await ContainerStreamInfoFormatter.WriteAsync(track, output, cancellationToken);
                }
                if (stream.ColorDescription is not null)
                {
                    await VideoColorDescriptionFormatter.WriteAsync(stream.ColorDescription, output);
                }
                if (info.StreamMetadata.TryGetValue(stream.StreamIndex, out StreamMetadata? metadata))
                {
                    await StreamMetadataFormatter.WriteAsync(metadata, output, cancellationToken);
                }
            }
            if (container.Tags is IReadOnlyList<MediaTag> tags)
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
        }
    }
}
