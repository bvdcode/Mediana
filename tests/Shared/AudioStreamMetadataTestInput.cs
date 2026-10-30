// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs.Aac;
using Mediana.Codecs.Flac;
using Mediana.Codecs.Mp3;
using Mediana.Codecs.Pcm;
using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class AudioStreamMetadataTestInput
    {
        public static MediaEngine CreateEngine() => MediaProbeTestInput.CreateBuilder()
            .AddStreamMetadataReader(new PcmStreamMetadataReader())
            .AddStreamMetadataReader(new AiffPcmStreamMetadataReader())
            .AddStreamMetadataReader(new FlacStreamMetadataReader())
            .AddStreamMetadataReader(new Mp3StreamMetadataReader())
            .AddStreamMetadataReader(new AacStreamMetadataReader()).Build();

        public static async Task VerifyAsync(string root)
        {
            MediaEngine engine = CreateEngine();
            if (engine.AudioDecoderImplementations.Count != 0 || engine.VideoDecoderImplementations.Count != 0)
            {
                throw new InvalidOperationException("Reading codec headers must not require a decoder.");
            }
            foreach (string folder in new[] { "Wave", "Aiff" })
            {
                using JsonDocument references = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, folder, "references.json")));
                foreach (JsonElement entry in references.RootElement.EnumerateArray())
                {
                    JsonElement stream = entry.GetProperty("probe").GetProperty("streams")[0];
                    string name = entry.GetProperty("name").GetString()!;
                    string extension = folder switch
                    {
                        "Wave" => ".wav",
                        "Aiff" => ".aiff",
                        _ => throw new InvalidOperationException()
                    };
                    await VerifyFileAsync(engine, Path.Combine(root, folder, name + extension),
                        Rate(stream), stream.GetProperty("channels").GetInt32(), stream.GetProperty("bits_per_sample").GetInt32(),
                        expectChannelOrder: true);
                }
            }
            using (JsonDocument references = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "Flac", "decode-references.json"))))
            {
                foreach (JsonElement entry in references.RootElement.GetProperty("fixtures").EnumerateArray())
                {
                    await VerifyFileAsync(engine, Path.Combine(root, "Flac", entry.GetProperty("name").GetString() + ".flac"),
                        entry.GetProperty("sampleRate").GetInt32(), entry.GetProperty("channels").GetInt32(), entry.GetProperty("bitsPerSample").GetInt32());
                }
            }
            using (JsonDocument references = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "Mp3", "references.json"))))
            {
                foreach (JsonElement entry in references.RootElement.GetProperty("fixtures").EnumerateArray())
                {
                    await VerifyFileAsync(engine, Path.Combine(root, "Mp3", entry.GetProperty("name").GetString() + ".mp3"),
                        entry.GetProperty("sample_rate").GetInt32(), entry.GetProperty("channels").GetInt32(), null);
                }
            }
            using (JsonDocument references = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "Aac", "references.json"))))
            {
                foreach (JsonElement entry in references.RootElement.GetProperty("fixtures").EnumerateArray())
                {
                    JsonElement stream = entry.GetProperty("reference").GetProperty("streams")[0];
                    await VerifyFileAsync(engine, Path.Combine(root, "Aac", entry.GetProperty("name").GetString() + ".m4a"),
                        Rate(stream), stream.GetProperty("channels").GetInt32(), null);
                }
            }
            foreach (string file in new[] { "avc-aac.mp4", "avc-mp3-22.mp4", "avc-mp3-44.mp4" })
            {
                using JsonDocument reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "Mp4", file + ".json")));
                JsonElement stream = reference.RootElement.GetProperty("streams").EnumerateArray()
                    .Single(item => item.GetProperty("codec_name").GetString() is "aac" or "mp3");
                await VerifyFileAsync(engine, Path.Combine(root, "Mp4", file), Rate(stream), stream.GetProperty("channels").GetInt32(), null,
                    expectedIndex: stream.GetProperty("index").GetInt32());
            }
            using (JsonDocument reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "Metadata", "audio-headers.json"))))
            {
                string path = Path.Combine(root, "Metadata", reference.RootElement.GetProperty("input").GetString()!);
                byte[] bytes = await File.ReadAllBytesAsync(path);
                string expectedHash = reference.RootElement.GetProperty("inputSha256").GetString()!;
                if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The independent AAC surround input changed.");
                }
                JsonElement stream = reference.RootElement.GetProperty("reference").GetProperty("streams")[0];
                await VerifyFileAsync(engine, path, Rate(stream), stream.GetProperty("channels").GetInt32(), null);
                await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
                MediaInfo info = await engine.ProbeAsync(source);
                if (new AacDecoderFactory().GetUnsupportedReason(info.Streams[0]) is null)
                {
                    throw new InvalidOperationException("The surround metadata test must exercise a configuration outside decoder support.");
                }
            }
        }

        private static int Rate(JsonElement stream) => int.Parse(stream.GetProperty("sample_rate").GetString()!, CultureInfo.InvariantCulture);

        private static async Task VerifyFileAsync(MediaEngine engine, string path, int rate, int channels, int? bits,
            bool expectChannelOrder = false, int expectedIndex = 0)
        {
            MediaInfo info;
            using MemoryStream memory = new(await File.ReadAllBytesAsync(path));
            await using (RandomAccessStreamByteSource source = new(memory, leaveOpen: true))
            {
                await source.ReadAsync(new byte[3]);
                info = await engine.ProbeAsync(source);
                if (memory.Position != 3 || !memory.CanRead)
                {
                    throw new InvalidDataException("Codec metadata probing changed the source cursor or ownership.");
                }
            }
            if (info.StreamMetadata.Count != 1 || !info.StreamMetadata.TryGetValue(expectedIndex, out StreamMetadata? value)
                || value is not AudioStreamMetadata audio || audio.Kind != MediaKind.Audio || audio.SampleRate != rate
                || audio.ChannelCount != channels || audio.BitsPerSample != bits)
            {
                throw new InvalidDataException($"Audio header parameters differ from the independent reference for {Path.GetFileName(path)}.");
            }
            AudioStreamMetadata metadata = (AudioStreamMetadata)value;
            if (expectChannelOrder && metadata.ChannelOrder?.Count != channels)
            {
                throw new InvalidDataException("PCM metadata lost its explicit channel order.");
            }
            if (!expectChannelOrder && metadata.ChannelOrder is not null)
            {
                throw new InvalidDataException("An unestablished channel order was guessed.");
            }
        }
    }
}
