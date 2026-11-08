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
    internal static class AacSbrDecodeTestInput
    {
        public static async Task VerifySequenceAsync(string root, int channels, bool explicitSbr, bool singleRate = false)
        {
            byte[][] packets = await ReadPacketsAsync(root, channels);
            byte[] expected = await ReadPcmAsync(root, channels, singleRate: singleRate);
            int samplesPerFrame = singleRate ? 1024 : 2048;
            int sampleRate = singleRate ? 24000 : 48000;
            EncodedStreamInfo stream = singleRate ? SingleRateStream(channels, explicitSbr) : Stream(channels, explicitSbr);
            Require(packets.Length == 16 && expected.Length == 16 * samplesPerFrame * channels * sizeof(float));
            MediaBufferAllocator allocator = new(256 * 1024);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(stream, allocator))
            {
                Require(decoder is IPacketPrerollDecoder preroll && preroll.RequiredPrerollPackets == (explicitSbr || singleRate ? 2 : 1));
                double error = 0;
                double energy = 0;
                double maximum = 0;
                long persistentBytes = 0;
                for (int index = 0; index < packets.Length; index++)
                {
                    using MediaPacket packet = Packet(allocator, packets[index], index);
                    Require(decoder.TrySend(packet) == CodecInputStatus.Accepted);
                    Require(decoder.TrySend(packet) == CodecInputStatus.OutputRequired);
                    using AudioFrame frame = decoder.Receive().Value;
                    Require(frame.Format.SampleRate == sampleRate && frame.Format.Channels.Count == channels && frame.SamplesPerChannel == samplesPerFrame
                        && frame.Duration == new MediaDuration(samplesPerFrame, new TimeBase(1, sampleRate)) && frame.Timestamp == packet.PresentationTime);
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
                    long current = allocator.AllocatedBytes - packet.Data.Length - frame.GetPlane(0).Length;
                    if (index == 0)
                    {
                        persistentBytes = current;
                    }
                    Require(persistentBytes == current);
                }
                Require(10 * Math.Log10(energy / error) > 80 && maximum <= 0.00025 && persistentBytes is > 0 and <= 240 * 1024);
                decoder.CompleteInput();
                Require(decoder.Receive().Status == CodecOutputStatus.EndOfStream);
            }
            Require(allocator.AllocatedBytes == 0);
        }

        public static EncodedStreamInfo Stream(int channels, bool explicitSbr)
        {
            byte[] configuration = explicitSbr ? channels switch
            {
                1 => [0x2b, 0x09, 0x88, 0],
                2 => [0x2b, 0x11, 0x88, 0],
                _ => throw new ArgumentOutOfRangeException(nameof(channels))
            } : AacConfiguration.Create(24000, channels).ToAudioSpecificConfig();
            return new(0, MediaKind.Audio, "aac", StreamFormats.Aac, new TimeBase(1, 24000), configuration);
        }

        public static EncodedStreamInfo SingleRateStream(int channels, bool explicitSbr)
        {
            byte[] configuration = (channels, explicitSbr) switch
            {
                (1, true) => [0x2b, 0x0b, 0x08, 0],
                (2, true) => [0x2b, 0x13, 0x08, 0],
                (1, false) => [0x13, 0x08, 0x56, 0xe5, 0xb5, 0x48, 0],
                (2, false) => [0x13, 0x10, 0x56, 0xe5, 0xb5, 0x48, 0],
                _ => throw new ArgumentOutOfRangeException(nameof(channels))
            };
            return new(0, MediaKind.Audio, "aac", StreamFormats.Aac, new TimeBase(1, 24000), configuration);
        }

        public static MediaPacket Packet(MediaBufferAllocator allocator, byte[] bytes, int index)
        {
            using WritableMediaBuffer writable = allocator.Allocate(bytes.Length);
            bytes.CopyTo(writable.Memory.Span);
            using MediaBuffer buffer = writable.Publish();
            return new(0, buffer, new MediaTimestamp(index * 1024, new TimeBase(1, 24000)));
        }

        public static async Task<byte[][]> ReadPacketsAsync(string root, int channels)
        {
            string name = Name(channels);
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

        public static Task<byte[]> ReadPcmAsync(string root, int channels, bool restart = false, bool singleRate = false)
        {
            (string suffix, string property) = (restart, singleRate) switch
            {
                (false, false) => (".f32le.gz", "pcmSha256"),
                (true, false) => ("-rap.f32le.gz", "restartPcmSha256"),
                (false, true) => ("-single-rate.f32le.gz", "singleRatePcmSha256"),
                (true, true) => ("-single-rate-rap.f32le.gz", "singleRateRestartPcmSha256")
            };
            return ReadReferenceAsync(root, channels, suffix, property);
        }

        private static async Task<byte[]> ReadReferenceAsync(string root, int channels, string suffix, string property)
        {
            string name = Name(channels);
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

        private static string Name(int channels) => channels switch
        {
            1 => "transients-mono",
            2 => "transients-stereo",
            _ => throw new ArgumentOutOfRangeException(nameof(channels))
        };

        private static void Require(bool value)
        {
            if (!value)
            {
                throw new InvalidDataException("The HE-AAC reference, timing, PCM quality, state lifetime or buffer ownership check failed.");
            }
        }
    }
}
