// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Codecs.H264;
using Mediana.Codecs.Mp3;
using Mediana.Containers.Matroska;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats.Audio;
using Mediana.IO;
using static Mediana.Tests.MatroskaMetadataTestData;
using static Mediana.Tests.MatroskaIndexTestData;
using static Mediana.Tests.MatroskaPacketTestData;

namespace Mediana.Tests
{
    internal static class MatroskaPacketReferenceInput
    {
        public static async Task VerifyAsync()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Matroska");
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "decode-references.json")));
            int total = 0;
            foreach (JsonElement fixture in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
            {
                Console.WriteLine($"Matroska input: {fixture.GetProperty("input").GetString()}");
                total += await VerifyFileAsync(root, fixture);
            }
            Require(total == 178);
        }

        private static async Task<int> VerifyFileAsync(string root, JsonElement fixture)
        {
            string name = fixture.GetProperty("input").GetString()!;
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(root, name));
            VerifyHash(bytes, fixture.GetProperty("input_sha256").GetString());
            byte[] audioReference = await ReadPcmAsync(root, fixture);
            bool mpeg = name == "mp3-low-rate.mka";
            string packetPath = Path.ChangeExtension(name, mpeg ? ".external.json" : ".packets.json");
            using JsonDocument packetReference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, packetPath)));
            JsonElement expectedPackets = packetReference.RootElement.GetProperty("packets");
            string metadataPath = Path.ChangeExtension(name, mpeg ? ".external.json" : ".metadata.json");
            using JsonDocument metadataReference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, metadataPath)));
            JsonElement[] streamReferences = metadataReference.RootElement.GetProperty(mpeg ? "streams" : "external_streams")
                .EnumerateArray().Where(stream => stream.GetProperty("codec_type").GetString() is "audio" or "video").ToArray();
            MediaBufferAllocator allocator = new(2 * 1024 * 1024);
            await using EbmlElementTestSource source = new(bytes) { MaximumRead = 7 };
            (MatroskaHeader header, EbmlElement segment, MatroskaSegmentInfo info, IReadOnlyList<MatroskaTrack> tracks) =
                await ReadMetadataAsync(source, allocator);
            using (MatroskaPacketIndex index = await MatroskaPacketIndexBuilder.ReadAsync(source, allocator, segment, tracks, Options, Limits, header.MaximumSizeWidth))
            {
                IReadOnlyList<MatroskaCodecConfiguration> configurations = await ReadCodecsAsync(source, allocator, tracks, index);
                using MatroskaPacketSource packets = new(source, allocator, index, tracks, configurations, info.TimestampScale, Options);
                int audioStream = configurations.Single(configuration => configuration.Stream.Kind == MediaKind.Audio).Stream.StreamIndex;
                EncodedStreamInfo audio = configurations[audioStream].Stream;
                using IMediaCodec<MediaPacket, AudioFrame> audioDecoder = audio.CodecId switch
                {
                    "aac" => new AacDecoderFactory().Create(audio, allocator),
                    "mp3" => new Mp3DecoderFactory().Create(audio, allocator),
                    _ => throw new InvalidDataException("Unexpected reference audio codec.")
                };
                AacConfiguration? aac = audio.CodecId == "aac" ? AacConfiguration.ReadAudioSpecificConfig(audio.Configuration.Span, out _) : null;
                using AacDecoderWorkspace? inspection = aac is null ? null : new(allocator, new AacChannelLayout(aac.ChannelConfiguration));
                uint noiseState = 0x13579bdf;
                bool hasNoise = false;
                byte[] actualAudio = new byte[audioReference.Length];
                EncodedStreamInfo? video = configurations.SingleOrDefault(configuration => configuration.Stream.Kind == MediaKind.Video)?.Stream;
                using IMediaCodec<MediaPacket, VideoFrame>? videoDecoder = video is null ? null : new H264DecoderFactory().Create(video, allocator);
                byte[] videoReference = video is null ? [] : await File.ReadAllBytesAsync(Path.Combine(root, "..", "Mp4", "avc-aac.sequence.yuv"));
                MediaTimestamp[] videoTimes = expectedPackets.EnumerateArray().Where(packet =>
                    packet.TryGetProperty("track_number", out JsonElement number) && video is not null && number.GetUInt64() == tracks[video.StreamIndex].Number)
                    .Select(packet => ReadTimestamp(packet.GetProperty("pts").GetInt64(), streamReferences[video!.StreamIndex]))
                    .Order().ToArray();
                for (int stream = 0; stream < tracks.Count; stream++)
                {
                    JsonElement[] expectedTrack = expectedPackets.EnumerateArray().Where(packet => mpeg ||
                        packet.GetProperty("track_number").GetUInt64() == tracks[stream].Number).ToArray();
                    for (int packetIndex = expectedTrack.Length - 1; packetIndex >= 0; packetIndex--)
                    {
                        using MediaPacket packet = await packets.ReadPacketAtAsync(stream, packetIndex);
                        VerifyPacket(packet, expectedTrack[packetIndex], mpeg);
                    }
                }
                long[] positions = new long[tracks.Count];
                int packetCount = 0;
                int audioBytes = 0;
                int audioFrames = 0;
                int videoFrames = 0;
                while (await packets.ReadPacketAsync() is MediaPacket packet)
                {
                    using (packet)
                    {
                        JsonElement expected = expectedPackets[packetCount++];
                        VerifyPacket(packet, expected, mpeg);
                        MatroskaPacketAddress address = index.Locate(packet.StreamIndex, positions[packet.StreamIndex]++);
                        if (address.FrameIndex == 0)
                        {
                            Require(packet.PresentationTime == ReadTimestamp(expected.GetProperty("pts").GetInt64(), streamReferences[packet.StreamIndex]));
                        }
                        else
                        {
                            long ns = (address.FrameIndex * 1024L * 1000000000 + 24000) / 48000;
                            Require(packet.PresentationTime == new MediaTimestamp(ns, MatroskaTrackClock.TimeBase));
                        }
                        if (packet.StreamIndex == audioStream)
                        {
                            if (inspection is not null)
                            {
                                AacAccessUnitParser.Read(packet.Data.Span, aac!.FrequencyIndex, inspection, ref noiseState, default);
                                hasNoise |= inspection.Channels.Any(channel => channel.Books.Contains((byte)13));
                            }
                            Require(audioDecoder.TrySend(packet) == CodecInputStatus.Accepted);
                            CodecOutput<AudioFrame> output = audioDecoder.Receive();
                            Require(output.Status == CodecOutputStatus.Available);
                            using AudioFrame frame = output.Value;
                            Require(frame.Timestamp == packet.PresentationTime);
                            Require(frame.Format.SampleRate == fixture.GetProperty("sample_rate").GetInt32());
                            Require(frame.Format.Channels.Count == fixture.GetProperty("channels").GetInt32());
                            frame.GetPlane(0).Span.CopyTo(actualAudio.AsSpan(audioBytes));
                            audioBytes += frame.GetPlane(0).Length;
                            audioFrames++;
                            Require(audioDecoder.Receive().Status == CodecOutputStatus.InputRequired);
                        }
                        else
                        {
                            Require(videoDecoder!.TrySend(packet) == CodecInputStatus.Accepted);
                            videoFrames = DrainVideo(videoDecoder, videoReference, videoTimes, videoFrames);
                        }
                    }
                }
                audioDecoder.CompleteInput();
                Require(audioDecoder.Receive().Status == CodecOutputStatus.EndOfStream);
                Require(audioBytes == audioReference.Length && audioFrames == fixture.GetProperty("audio_packets").GetInt32());
                VerifyAudio(actualAudio, audioReference, hasNoise, fixture.GetProperty("channels").GetInt32());
                if (aac is not null)
                {
                    await VerifySourceAudioAsync(root, actualAudio, name == "aac-lace-fixed.mka");
                }
                if (videoDecoder is not null)
                {
                    videoDecoder.CompleteInput();
                    videoFrames = DrainVideo(videoDecoder, videoReference, videoTimes, videoFrames);
                    Require(videoFrames == 8 && videoDecoder.Receive().Status == CodecOutputStatus.EndOfStream);
                }
                Require(packetCount == expectedPackets.GetArrayLength() && packetCount == positions.Sum());
            }
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
            return expectedPackets.GetArrayLength();
        }

        private static void VerifyPacket(MediaPacket packet, JsonElement expected, bool mpeg)
        {
            string hash = mpeg ? expected.GetProperty("data_hash").GetString()!["SHA256:".Length..] : expected.GetProperty("sha256").GetString()!;
            VerifyHash(packet.Data.Span, hash);
            int size = mpeg ? int.Parse(expected.GetProperty("size").GetString()!, CultureInfo.InvariantCulture) : expected.GetProperty("size").GetInt32();
            Require(packet.Data.Length == size && packet.DecodeTime is null);
        }

        private static void VerifyAudio(byte[] actual, byte[] expected, bool hasNoise, int channels)
        {
            ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(actual);
            for (int index = 0; index < samples.Length; index++)
            {
                float reference = BinaryPrimitives.ReadSingleLittleEndian(expected.AsSpan(index * 4, 4));
                if (!float.IsFinite(samples[index]) || !float.IsFinite(reference) || (!hasNoise && Math.Abs(samples[index] - reference) > 2e-6))
                {
                    throw new InvalidDataException($"Matroska decoded PCM differs at byte {index * 4}: {samples[index]:G9}, expected {reference:G9}.");
                }
            }
            if (hasNoise)
            {
                (double actualEnergy, double expectedEnergy) = AacDecodeTestInput.GetVerifiedNoiseEnergy(actual, expected, channels);
                Console.WriteLine($"Matroska PNS total relative energy difference: {(actualEnergy - expectedEnergy) / expectedEnergy:G9}.");
            }
        }

        private static async Task VerifySourceAudioAsync(string root, byte[] actual, bool repeatFirst)
        {
            MediaBufferAllocator allocator = new(1048576);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, "..", "Mp4", "avc-aac.mp4")));
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                EncodedStreamInfo stream = reader.Streams.Single(value => value.Kind == MediaKind.Audio);
                using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(stream, allocator);
                int packetIndex = 0;
                int offset = 0;
                while (offset < actual.Length)
                {
                    using MediaPacket packet = await reader.ReadPacketAtAsync(stream.StreamIndex, repeatFirst ? 0 : packetIndex++);
                    Require(decoder.TrySend(packet) == CodecInputStatus.Accepted);
                    CodecOutput<AudioFrame> output = decoder.Receive();
                    Require(output.Status == CodecOutputStatus.Available);
                    using AudioFrame frame = output.Value;
                    Require(frame.GetPlane(0).Span.SequenceEqual(actual.AsSpan(offset, frame.GetPlane(0).Length)));
                    offset += frame.GetPlane(0).Length;
                    Require(decoder.Receive().Status == CodecOutputStatus.InputRequired);
                }
                Require(offset == actual.Length);
                decoder.CompleteInput();
                Require(decoder.Receive().Status == CodecOutputStatus.EndOfStream);
            }
            Require(allocator.AllocatedBytes == 0);
        }

        private static int DrainVideo(IMediaCodec<MediaPacket, VideoFrame> decoder, byte[] expected, MediaTimestamp[] times, int frameIndex)
        {
            while (true)
            {
                CodecOutput<VideoFrame> output = decoder.Receive();
                if (output.Status != CodecOutputStatus.Available)
                {
                    return frameIndex;
                }
                using VideoFrame frame = output.Value;
                const int frameBytes = 64 * 48 * 3 / 2;
                H264TestInput.VerifyFrame(frame, expected.AsSpan(frameIndex * frameBytes, frameBytes));
                Require(frame.Timestamp == times[frameIndex] && frame.Duration == new MediaDuration(200000000, MatroskaTrackClock.TimeBase));
                frameIndex++;
            }
        }

        private static async Task<byte[]> ReadPcmAsync(string root, JsonElement fixture)
        {
            byte[] packed = await File.ReadAllBytesAsync(Path.Combine(root, fixture.GetProperty("reference").GetString()!));
            VerifyHash(packed, fixture.GetProperty("reference_sha256").GetString());
            using MemoryStream input = new(packed, writable: false);
            await using GZipStream compressed = new(input, CompressionMode.Decompress);
            using MemoryStream output = new();
            await compressed.CopyToAsync(output);
            byte[] pcm = output.ToArray();
            Require(pcm.Length == fixture.GetProperty("pcm_bytes").GetInt32());
            VerifyHash(pcm, fixture.GetProperty("pcm_sha256").GetString());
            return pcm;
        }

        private static MediaTimestamp ReadTimestamp(long value, JsonElement stream)
        {
            string[] terms = stream.GetProperty("time_base").GetString()!.Split('/');
            return new(value, new(int.Parse(terms[0], CultureInfo.InvariantCulture), int.Parse(terms[1], CultureInfo.InvariantCulture)));
        }

        private static void VerifyHash(ReadOnlySpan<byte> bytes, string? expected) => Require(Convert.ToHexStringLower(SHA256.HashData(bytes)) == expected);
    }
}
