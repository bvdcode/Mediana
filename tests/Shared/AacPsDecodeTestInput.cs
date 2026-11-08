// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    internal static class AacPsDecodeTestInput
    {
        public static async Task VerifySequenceAsync(string root, string name, bool explicitPs, bool singleRate = false)
        {
            byte[][] packets = await ReadPacketsAsync(root, name);
            byte[] expected = await ReadReferenceAsync(root, name, singleRate: singleRate);
            int frames = name switch
            {
                "tonal" => 27,
                "transients" or "parameters" => 51,
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };
            int samples = singleRate ? 1024 : 2048;
            int sampleRate = singleRate ? 24000 : 48000;
            Require(packets.Length == frames && expected.Length == frames * samples * 2 * sizeof(float));
            MediaBufferAllocator allocator = new(256 * 1024);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(Stream(explicitPs, singleRate), allocator))
            {
                double error = 0;
                double energy = 0;
                double maximum = 0;
                long persistent = 0;
                for (int index = 0; index < packets.Length; index++)
                {
                    using MediaPacket packet = AacSbrDecodeTestInput.Packet(allocator, packets[index], index);
                    Require(decoder.TrySend(packet) == CodecInputStatus.Accepted);
                    using AudioFrame frame = decoder.Receive().Value;
                    Require(frame.Format.SampleRate == sampleRate && frame.Format.Channels.Count == 2 && frame.SamplesPerChannel == samples
                        && frame.Duration == new MediaDuration(samples, new TimeBase(1, sampleRate)) && frame.Timestamp == packet.PresentationTime);
                    ReadOnlySpan<byte> actual = frame.GetPlane(0).Span;
                    int offset = index * actual.Length;
                    for (int position = 0; position < actual.Length; position += sizeof(float))
                    {
                        double value = BinaryPrimitives.ReadSingleLittleEndian(actual[position..]);
                        double reference = BinaryPrimitives.ReadSingleLittleEndian(expected.AsSpan(offset + position));
                        Require(double.IsFinite(value));
                        double difference = value - reference;
                        error += difference * difference;
                        energy += reference * reference;
                        maximum = Math.Max(maximum, Math.Abs(difference));
                    }
                    long current = allocator.AllocatedBytes - packet.Data.Length - actual.Length;
                    if (index == 0)
                    {
                        persistent = current;
                    }
                    Require(persistent == current);
                }
                (double minimumRatio, double maximumError) = name switch
                {
                    "tonal" => (60.0, 0.001),
                    "transients" or "parameters" => (95.0, 0.0001),
                    _ => throw new ArgumentOutOfRangeException(nameof(name))
                };
                Require(10 * Math.Log10(energy / error) >= minimumRatio && maximum <= maximumError && persistent is > 0 and < 240 * 1024);
                decoder.CompleteInput();
                Require(decoder.Receive().Status == CodecOutputStatus.EndOfStream);
            }
            Require(allocator.AllocatedBytes == 0);
        }

        public static EncodedStreamInfo Stream(bool explicitPs, bool singleRate = false)
        {
            byte[] configuration = (explicitPs, singleRate) switch
            {
                (true, false) => [0xeb, 0x09, 0x88, 0],
                (true, true) => [0xeb, 0x0b, 0x08, 0],
                (false, true) => [0x13, 0x08, 0x56, 0xe5, 0xb5, 0x48, 0x80],
                (false, false) => AacConfiguration.Create(24000, 1).ToAudioSpecificConfig()
            };
            return new(0, MediaKind.Audio, "aac", StreamFormats.Aac, new TimeBase(1, 24000), configuration);
        }

        public static async Task<byte[][]> ReadPacketsAsync(string root, string name)
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(root, name + ".aac"));
            await VerifyHashAsync(root, name, bytes, "inputSha256");
            List<byte[]> packets = [];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = ((bytes[offset + 3] & 3) << 11) | (bytes[offset + 4] << 3) | (bytes[offset + 5] >> 5);
                packets.Add(bytes.AsSpan(offset + 7, count - 7).ToArray());
                offset += count;
            }
            Require(bytes.Length == offset);
            return packets.ToArray();
        }

        public static async Task<byte[]> ReadReferenceAsync(string root, string name, bool restart = false, bool singleRate = false)
        {
            (string suffix, string property) = (restart, singleRate) switch
            {
                (false, false) => (".f32le.gz", "pcmSha256"),
                (true, false) => ("-rap.f32le.gz", "restartPcmSha256"),
                (false, true) => ("-single-rate.f32le.gz", "singleRatePcmSha256"),
                (true, true) => throw new NotSupportedException("A single-rate PS restart reference is not provided.")
            };
            await using FileStream file = File.OpenRead(Path.Combine(root, name + suffix));
            await using GZipStream gzip = new(file, CompressionMode.Decompress);
            await using MemoryStream output = new();
            await gzip.CopyToAsync(output);
            byte[] bytes = output.ToArray();
            await VerifyHashAsync(root, name, bytes, property);
            return bytes;
        }

        private static async Task VerifyHashAsync(string root, string name, byte[] bytes, string property)
        {
            using JsonDocument document = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "references.json")));
            JsonElement fixture = document.RootElement.GetProperty("fixtures").EnumerateArray().Single(value => value.GetProperty("name").GetString() == name);
            Require(Convert.ToHexStringLower(SHA256.HashData(bytes)) == fixture.GetProperty(property).GetString());
        }

        private static void Require(bool value)
        {
            if (!value)
            {
                throw new InvalidDataException("The HE-AAC v2 reference, PCM quality, timing, state lifetime or buffer ownership check failed.");
            }
        }
    }
}
