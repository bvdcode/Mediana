// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs.Mp3;
using Mediana.Containers.Mp3;
using Mediana.Core.Audio;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class Mp3DecodeTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "pcm-references.json")));
            foreach (JsonElement item in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
            {
                await VerifyFixtureAsync(root, item);
            }
        }

        private static async Task VerifyFixtureAsync(string root, JsonElement item)
        {
            string name = item.GetProperty("name").GetString()!;
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, name + ".mp3"));
            VerifyHash(input, item.GetProperty("inputSha256").GetString());
            using MemoryStream reference = new();
            await using (FileStream file = File.OpenRead(Path.Combine(root, name + ".f32le.gz")))
            await using (GZipStream compressed = new(file, CompressionMode.Decompress))
            {
                await compressed.CopyToAsync(reference);
            }
            byte[] expected = reference.ToArray();
            VerifyHash(expected, item.GetProperty("pcmSha256").GetString());
            int rate = item.GetProperty("rate").GetInt32();
            int channels = item.GetProperty("channels").GetInt32();
            int samplesPerFrame = item.GetProperty("samplesPerFrame").GetInt32();
            int expectedFrames = item.GetProperty("frames").GetInt32();
            double tolerance = item.GetProperty("maximumError").GetDouble();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp3ReaderFactory())
                .AddAudioDecoder(new Mp3DecoderFactory()).Build();
            using MemoryStream stream = new(input, writable: false);
            await using RandomAccessStreamByteSource source = new(stream, leaveOpen: true);
            await source.ReadAsync(new byte[3]);
            await using AudioSession session = await engine.OpenAudioAsync(source, 0, new MediaOpenOptions { BufferBudgetBytes = 128 * 1024 });
            AudioFrame? retained = null;
            byte[]? last = null;
            int frames = 0;
            int offset = 0;
            try
            {
                while (true)
                {
                    using AudioFrame? frame = await session.ReadFrameAsync();
                    if (frame is null)
                    {
                        break;
                    }
                    if (frame.Format.SampleRate != rate || frame.Format.SampleFormat != AudioSampleFormat.Float32
                        || frame.Format.Layout != AudioStorageLayout.Interleaved || frame.Format.Channels.Count != channels
                        || frame.SamplesPerChannel != samplesPerFrame
                        || frame.Timestamp != new MediaTimestamp((long)frames * samplesPerFrame, new TimeBase(1, rate))
                        || frame.Duration != new MediaDuration(samplesPerFrame, new TimeBase(1, rate)))
                    {
                        throw new InvalidDataException($"MP3 format or timing differs in {name}, frame {frames}.");
                    }
                    ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(frame.GetPlane(0).Span);
                    if (samples.Length * sizeof(float) > expected.Length - offset)
                    {
                        throw new InvalidDataException("MP3 produced more PCM than the independent reference.");
                    }
                    for (int index = 0; index < samples.Length; index++)
                    {
                        float value = BinaryPrimitives.ReadSingleLittleEndian(expected.AsSpan(offset + index * sizeof(float), sizeof(float)));
                        if (!float.IsFinite(samples[index]) || !float.IsFinite(value) || Math.Abs(samples[index] - value) > tolerance)
                        {
                            throw new InvalidDataException($"MP3 PCM differs in {name}, frame {frames}, sample {index}.");
                        }
                    }
                    offset += frame.GetPlane(0).Length;
                    retained?.Dispose();
                    retained = frame.Retain();
                    last = frame.GetPlane(0).ToArray();
                    frames++;
                }
                if (frames != expectedFrames || offset != expected.Length || await session.ReadFrameAsync() is not null)
                {
                    throw new InvalidDataException("MP3 sample count or end of stream differs.");
                }
                await session.DisposeAsync();
                if (retained is null || last is null || session.AllocatedBufferBytes != last.Length
                    || !retained.GetPlane(0).Span.SequenceEqual(last) || stream.Position != 3)
                {
                    throw new InvalidOperationException("MP3 retained output or source ownership differs after disposal.");
                }
            }
            finally
            {
                retained?.Dispose();
            }
            if (session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("MP3 buffers remain allocated after disposal.");
            }
        }

        private static void VerifyHash(ReadOnlySpan<byte> data, string? expected)
        {
            if (Convert.ToHexStringLower(SHA256.HashData(data)) != expected)
            {
                throw new InvalidDataException("An MP3 PCM reference hash differs.");
            }
        }
    }
}
