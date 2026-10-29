// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs.Flac;
using Mediana.Codecs.Pcm;
using Mediana.Containers.Flac;
using Mediana.Containers.Wave;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    internal static class FlacWaveformTestInput
    {
        public static async Task VerifyAsync(string root, string name)
        {
            (string waveName, double[] gains, long count) = name switch
            {
                "stereo-covers" => ("music.wav", new double[] { 0.5, 0.5 }, 2292L),
                "mono-speech" => ("speech.wav", new double[] { 1.0 }, 1360L),
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };
            string wavePath = Path.Combine(root, "..", "Waveform", waveName);
            using JsonDocument references = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "references.json")));
            JsonElement entry = references.RootElement.GetProperty("fixtures").EnumerateArray()
                .Single(item => item.GetProperty("name").GetString() == name);
            if (Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(wavePath))) != entry.GetProperty("sourceSha256").GetString())
            {
                throw new InvalidDataException("The source WAVE no longer matches the original FLAC input.");
            }
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new FlacReaderFactory(new FlacReadOptions { PacketBytes = 127 }))
                .AddContainer(new WaveReaderFactory()).AddAudioDecoder(new FlacDecoderFactory()).AddAudioDecoder(new PcmDecoderFactory()).Build();
            await using RandomAccessStreamByteSource flacSource = new(File.OpenRead(Path.Combine(root, name + ".flac")));
            await using RandomAccessStreamByteSource waveSource = new(File.OpenRead(wavePath));
            await using AudioSession flac = await engine.OpenAudioAsync(flacSource, 0, new MediaOpenOptions { BufferBudgetBytes = 512 * 1024 });
            await using AudioSession wave = await engine.OpenAudioAsync(waveSource, 0, new MediaOpenOptions { BufferBudgetBytes = 128 * 1024 });
            MediaBufferAllocator processing = new(512 * 1024);
            AudioWaveformOptions options = new(400, 37)
            {
                SampleFormat = AudioSampleFormat.Signed16,
                Resampling = new AudioResampleOptions { CutoffRatio = 0.97, BoundaryMode = AudioBoundaryMode.Reflect }
            };
            await using AudioWaveformReader actual = new(flac.ReadFrameAsync, gains, options, processing);
            await using AudioWaveformReader expected = new(wave.ReadFrameAsync, gains, options, processing);
            long samples = 0;
            while (true)
            {
                AudioLevel? actualLevel = await actual.ReadLevelAsync();
                AudioLevel? expectedLevel = await expected.ReadLevelAsync();
                if (actualLevel != expectedLevel)
                {
                    throw new InvalidDataException("The complete FLAC 400 Hz waveform differs from its original PCM source.");
                }
                if (actualLevel is not AudioLevel level)
                {
                    break;
                }
                samples += level.SampleCount;
            }
            await actual.DisposeAsync();
            await expected.DisposeAsync();
            await flac.DisposeAsync();
            await wave.DisposeAsync();
            if (samples != count || processing.AllocatedBytes != 0 || flac.AllocatedBufferBytes != 0 || wave.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("FLAC waveform changed sample count or retained media storage.");
            }
        }
    }
}
