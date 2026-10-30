// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text.Json;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Cli
{
    internal static class StreamMetadataFormatter
    {
        public static Task WriteAsync(StreamMetadata metadata, TextWriter output, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return metadata switch
            {
                AudioStreamMetadata audio => WriteAudioAsync(audio, output),
                VideoStreamMetadata video => WriteVideoAsync(video, output, cancellationToken),
                _ => throw new NotSupportedException("The stream metadata description has no console formatter.")
            };
        }

        private static async Task WriteAudioAsync(AudioStreamMetadata audio, TextWriter output)
        {
            await output.WriteLineAsync($"  Audio: {audio.SampleRate} Hz; channels: {audio.ChannelCount}.");
            if (audio.BitsPerSample is int bits)
            {
                await output.WriteLineAsync($"  Sample precision: {bits} bits.");
            }
            if (audio.ChannelOrder is IReadOnlyList<string> channels)
            {
                string order = string.Join(", ", channels.Select(channel => $"\"{JsonEncodedText.Encode(channel)}\""));
                await output.WriteLineAsync($"  Channel order: [{order}].");
            }
        }

        private static async Task WriteVideoAsync(VideoStreamMetadata video, TextWriter output, CancellationToken cancellationToken)
        {
            await output.WriteLineAsync($"  Opening video configurations: {video.Configurations.Count}.");
            for (int index = 0; index < video.Configurations.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                VideoStreamConfiguration configuration = video.Configurations[index];
                string sampling = configuration.ChromaSampling switch
                {
                    VideoChromaSampling.Monochrome => "monochrome",
                    VideoChromaSampling.Sampling420 => "4:2:0",
                    VideoChromaSampling.Sampling422 => "4:2:2",
                    VideoChromaSampling.Sampling444 => "4:4:4",
                    _ => throw new ArgumentOutOfRangeException(nameof(video), "The video chroma sampling is invalid.")
                };
                await output.WriteLineAsync($"  Configuration {index + 1}: {configuration.Width}x{configuration.Height} visible samples; chroma: {sampling}.");
                if (configuration.ChromaBitDepth is int chromaDepth)
                {
                    await output.WriteLineAsync($"    Video precision: luma {configuration.LumaBitDepth} bits; chroma {chromaDepth} bits.");
                }
                else
                {
                    await output.WriteLineAsync($"    Video precision: luma {configuration.LumaBitDepth} bits.");
                }
                string scan = configuration.IsProgressive switch
                {
                    true => "progressive",
                    false => "interlaced",
                    null => "unknown"
                };
                await output.WriteLineAsync($"    Scan: {scan}.");
                if (configuration.SampleAspectRatio is SampleAspectRatio aspect)
                {
                    await output.WriteLineAsync($"    Sample aspect ratio: {aspect.Numerator}:{aspect.Denominator}.");
                }
                VideoColorInfo color = configuration.Color;
                await output.WriteLineAsync($"    Color: primaries {color.Primaries}; transfer {color.Transfer}; matrix {color.Matrix}; range {color.Range}; chroma location {color.ChromaLocation}.");
            }
        }
    }
}
