// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs.Flac;
using Mediana.Containers.Flac;
using Mediana.Core.Audio;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class FlacDecodeTestInput
    {
        public static readonly string[] Names = ["stereo-covers", "mono-speech", "stereo24", "surround24", "stereo32"];

        public static async Task VerifyAsync(string root, string name, int packetBytes)
        {
            using JsonDocument document = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "decode-references.json")));
            JsonElement entry = document.RootElement.GetProperty("fixtures").EnumerateArray()
                .Single(value => value.GetProperty("name").GetString() == name);
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, name + ".flac"));
            if (Convert.ToHexStringLower(SHA256.HashData(input)) != entry.GetProperty("inputSha256").GetString())
            {
                throw new InvalidDataException("The FLAC input differs from its independent reference.");
            }
            int rate = entry.GetProperty("sampleRate").GetInt32();
            int channels = entry.GetProperty("channels").GetInt32();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new FlacReaderFactory(new FlacReadOptions { PacketBytes = packetBytes }))
                .AddAudioDecoder(new FlacDecoderFactory()).Build();
            await using MemoryStream file = new(input, writable: false);
            await using RandomAccessStreamByteSource source = new(file, leaveOpen: true);
            await source.ReadAsync(new byte[3]);
            await using AudioSession session = await engine.OpenAudioAsync(source, 0, new MediaOpenOptions { BufferBudgetBytes = 512 * 1024 });
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            AudioFrame? retained = null;
            byte[]? last = null;
            long samples = 0;
            try
            {
                while (true)
                {
                    using AudioFrame? frame = await session.ReadFrameAsync();
                    if (frame is null)
                    {
                        break;
                    }
                    if (frame.Format.SampleRate != rate || frame.Format.Channels.Count != channels
                        || frame.Format.SampleFormat != AudioSampleFormat.Signed32 || frame.Format.Layout != AudioStorageLayout.Interleaved
                        || frame.Timestamp != new MediaTimestamp(samples, new TimeBase(1, rate))
                        || frame.Duration != new MediaDuration(frame.SamplesPerChannel, new TimeBase(1, rate)))
                    {
                        throw new InvalidDataException("The FLAC PCM format, count or sample clock changed.");
                    }
                    byte[] littleEndian = new byte[frame.GetPlane(0).Length];
                    ReadOnlySpan<int> values = MemoryMarshal.Cast<byte, int>(frame.GetPlane(0).Span);
                    for (int i = 0; i < values.Length; i++)
                    {
                        BinaryPrimitives.WriteInt32LittleEndian(littleEndian.AsSpan(i * 4), values[i]);
                    }
                    hash.AppendData(littleEndian);
                    last = frame.GetPlane(0).ToArray();
                    retained?.Dispose();
                    retained = frame.Retain();
                    samples += frame.SamplesPerChannel;
                }
                if (samples != entry.GetProperty("samples").GetInt64()
                    || Convert.ToHexStringLower(hash.GetHashAndReset()) != entry.GetProperty("pcmSha256").GetString()
                    || await session.ReadFrameAsync() is not null)
                {
                    throw new InvalidDataException("The complete FLAC PCM differs from the independent lossless reference.");
                }
                await session.DisposeAsync();
                if (retained is null || last is null || session.AllocatedBufferBytes != last.Length
                    || !retained.GetPlane(0).Span.SequenceEqual(last) || file.Position != 3)
                {
                    throw new InvalidOperationException("FLAC changed retained output or borrowed source ownership.");
                }
            }
            finally
            {
                retained?.Dispose();
            }
            if (session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("FLAC did not release all media buffers.");
            }
        }
    }
}
