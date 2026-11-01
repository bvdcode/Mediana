// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class AacMultichannelTestInput
    {
        public static Task VerifyAsync(string root, int configuration) => VerifyAsync(root, $"layout-{configuration}", configuration);

        public static Task VerifyTransitionsAsync(string root) => VerifyAsync(root, "window-transitions", 2);

        private static async Task VerifyAsync(string root, string name, int configuration)
        {
            using JsonDocument document = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "references.json")));
            JsonElement entry = document.RootElement.GetProperty("fixtures").EnumerateArray()
                .Single(value => value.GetProperty("name").GetString() == name);
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, name + ".m4a"));
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + ".f32le"));
            if (Convert.ToHexStringLower(SHA256.HashData(input)) != entry.GetProperty("inputSha256").GetString() ||
                Convert.ToHexStringLower(SHA256.HashData(expected)) != entry.GetProperty("pcmSha256").GetString())
            {
                throw new InvalidDataException("The multichannel AAC reference hash changed.");
            }
            MediaBufferAllocator allocator = new(128 * 1024);
            await using (MemoryStream stream = new(input, writable: false))
            await using (RandomAccessStreamByteSource source = new(stream))
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                AacConfiguration actual = AacConfiguration.ReadAudioSpecificConfig(reader.Streams[0].Configuration.Span, out _)
                    ?? throw new InvalidDataException("The multichannel fixture has an unsupported configuration.");
                if (actual.ChannelConfiguration != configuration || actual.ChannelCount != entry.GetProperty("channels").GetInt32())
                {
                    throw new InvalidDataException("The multichannel fixture no longer declares its recorded speaker layout.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Multichannel configuration inspection retained media storage.");
            }
            await AacDecodeTestInput.VerifyAsync(input, expected, entry.GetProperty("reference").GetProperty("packets"),
                entry.GetProperty("sampleRate").GetInt32(), entry.GetProperty("channels").GetInt32(), 0, false);
        }
    }
}
