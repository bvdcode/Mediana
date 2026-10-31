// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class ServerAudioWaveformTestInput
    {
        public static async Task VerifyAsync(string root, string name)
        {
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "references.json")));
            JsonElement entry = manifest.RootElement.GetProperty("files").EnumerateArray()
                .Single(item => item.GetProperty("name").GetString() == name);
            string input = Path.Combine(root, name + ".wav");
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + ".s16le"));
            if (Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(input))) != entry.GetProperty("inputSha256").GetString()
                || Convert.ToHexStringLower(SHA256.HashData(reference)) != entry.GetProperty("referenceSha256").GetString())
            {
                throw new InvalidDataException("The server waveform input or independent reference changed.");
            }
            await using FileStream file = File.OpenRead(input);
            await using RandomAccessStreamByteSource source = new(file, leaveOpen: true);
            await source.ReadAsync(new byte[5]);
            using MemoryStream destination = new();
            await Sample.AudioWaveformSample.WriteAsync(Sample.AudioWaveformSample.CreateEngine(), source, destination);
            if (!destination.CanWrite || !file.CanRead || file.Position != 5)
            {
                throw new InvalidOperationException("The server waveform changed its borrowed streams' ownership or input position.");
            }
            using JsonDocument document = JsonDocument.Parse(destination.ToArray());
            JsonElement result = document.RootElement;
            int samples = reference.Length / sizeof(short);
            if (result.GetProperty("sampleRate").GetInt32() != 400
                || result.GetProperty("samplesPerWindow").GetInt32() != 40
                || result.GetProperty("sampleCount").GetInt64() != samples
                || result.GetProperty("windowCount").GetInt64() != (samples + 39) / 40)
            {
                throw new InvalidDataException("The server waveform changed the complete resampled duration.");
            }
            int position = 0;
            foreach (JsonElement level in result.GetProperty("windows").EnumerateArray())
            {
                int count = Math.Min(40, samples - position);
                double energy = 0;
                double peak = 0;
                for (int index = position; index < position + count; index++)
                {
                    double value = BinaryPrimitives.ReadInt16LittleEndian(reference.AsSpan(index * sizeof(short))) / 32768d;
                    energy += value * value;
                    peak = Math.Max(peak, Math.Abs(value));
                }
                if (count <= 0 || level.GetProperty("startSample").GetInt64() != position
                    || level.GetProperty("sampleCount").GetInt32() != count
                    || !double.IsFinite(level.GetProperty("rms").GetDouble())
                    || !double.IsFinite(level.GetProperty("peak").GetDouble())
                    || Math.Abs(level.GetProperty("rms").GetDouble() - Math.Sqrt(energy / count)) > 0.002
                    || Math.Abs(level.GetProperty("peak").GetDouble() - peak) > 0.002)
                {
                    throw new InvalidDataException("The server waveform differs from an independent PCM16 window.");
                }
                position += count;
            }
            if (position != samples)
            {
                throw new InvalidDataException("The server waveform lost its final partial window.");
            }
        }
    }
}
