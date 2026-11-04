// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Streaming.Hls;

namespace Mediana.Sample
{
    internal static class PreparedHlsSample
    {
        public static async Task<int> RunAsync(string input, string output, PreparedHlsSampleOptions options,
            CancellationToken token = default)
        {
            options.Validate();
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(output) || File.Exists(output))
            {
                throw new IOException("Choose a new directory for the prepared presentation.");
            }
            Directory.CreateDirectory(output);
            HlsPreparedAudio audio = await HlsSampleAudioPreparation.RunAsync(input, Path.Combine(output, "audio"), options, token);
            HlsVariant video = await HlsSampleVideoPreparation.RunAsync(input, Path.Combine(output, "video"), options, token);
            string[] repeats = Directory.GetFiles(Path.Combine(output, "video"), "repeat-*.ts");
            foreach (string repeated in repeats)
            {
                string original = Path.Combine(Path.GetDirectoryName(repeated)!, Path.GetFileName(repeated)["repeat-".Length..]);
                await using FileStream first = File.OpenRead(original);
                await using FileStream second = File.OpenRead(repeated);
                byte[] firstHash = await SHA256.HashDataAsync(first, token);
                byte[] secondHash = await SHA256.HashDataAsync(second, token);
                if (!firstHash.AsSpan().SequenceEqual(secondHash))
                {
                    throw new InvalidDataException("Repeating an independent video request changed its output.");
                }
            }
            await using (FileStream metadata = OpenDestination(output, "presentation.json"))
            await using (Utf8JsonWriter json = new(metadata, new() { Indented = true }))
            {
                json.WriteStartObject();
                json.WriteString("playlist", "index.m3u8");
                json.WriteNumber("startMilliseconds", 0);
                json.WriteNumber("endMilliseconds", options.EndMilliseconds);
                json.WriteNumber("audioAppendWindowEndSeconds", (decimal)options.EndMilliseconds / 1000);
                json.WriteNumber("selectedAudioSamplesPerChannel", audio.SamplesPerChannel);
                json.WriteNumber("audioSampleRate", audio.SampleRate);
                json.WriteNumber("audioPrimingSamples", audio.PrimingSamples);
                json.WriteNumber("audioPaddingSamples", audio.PaddingSamples);
                json.WriteEndObject();
                await json.FlushAsync(token);
            }
            await File.WriteAllTextAsync(Path.Combine(output, "index.m3u8"),
                HlsMasterPlaylist.Create([video], [audio.Rendition]), token);
            await Console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                $"Prepared HLS: 0..{options.EndMilliseconds} ms; {audio.SamplesPerChannel} audio samples/channel; AAC priming {audio.PrimingSamples}, padding {audio.PaddingSamples}. Live buffer bytes: 0."));
            return 0;
        }

        internal static FileStream OpenDestination(string directory, string name) => new(Path.Combine(directory, name),
            FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);

        internal static ValueTask<Stream> OpenSegmentAsync(string directory, string name, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<Stream>(OpenDestination(directory, name));
        }
    }
}
