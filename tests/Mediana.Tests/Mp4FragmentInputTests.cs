// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Containers.Mp4;
using Mediana.Codecs.Aac;
using Mediana.Core.Audio;
using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    public class Mp4FragmentInputTests
    {
        [Theory]
        [InlineData("fragment-relative.mp4", int.MaxValue, 0, 19200)]
        [InlineData("fragment-relative.mp4", 3, 0, 19200)]
        [InlineData("fragment-absolute.mp4", 31, 0, 19200)]
        [InlineData("fragment-implicit.mp4", 29, 0, 19200)]
        [InlineData("fragment-signed.mp4", 13, 2048, 1024)]
        [InlineData("fragment-hybrid.mp4", 17, 0, 19200)]
        public async Task IndependentFragmentsPreservePacketsTimingAndRandomAccessWithoutReadingPayloadAtOpen(string file, int maxRead, int referenceVideoPresentationShift, int firstAudioDuration)
        {
            byte[] bytes = await Mp4TestData.ReadAsync(file);
            using JsonDocument reference = JsonDocument.Parse(await Mp4TestData.ReadAsync(file + ".json"));
            Assert.Equal(reference.RootElement.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
            JsonElement[] packets = reference.RootElement.GetProperty("packets").EnumerateArray().ToArray();
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(bytes, maxRead);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            Assert.Equal(2, reader.Streams.Count);
            Assert.All(source.Reads, read => Assert.All(packets, packet =>
            {
                long start = long.Parse(packet.GetProperty("pos").GetString()!);
                int size = int.Parse(packet.GetProperty("size").GetString()!);
                Assert.True(read.Offset + read.Length <= start || read.Offset >= start + size);
            }));
            Dictionary<int, JsonElement[]> streams = packets.GroupBy(packet => packet.GetProperty("stream_index").GetInt32())
                .ToDictionary(group => group.Key, group => group.ToArray());
            int[] positions = new int[reader.Streams.Count];
            MediaTimestamp? previous = null;
            while (await reader.ReadPacketAsync() is MediaPacket packet)
            {
                using (packet)
                {
                    int index = positions[packet.StreamIndex]++;
                    VerifyPacket(streams[packet.StreamIndex][index], packet, referenceVideoPresentationShift,
                        packet.StreamIndex == 1 && index == 0 ? firstAudioDuration : null);
                    if (previous is MediaTimestamp before)
                    {
                        Assert.True(packet.DecodeTime!.Value.CompareTo(before) >= 0);
                    }
                    previous = packet.DecodeTime;
                }
            }
            foreach (KeyValuePair<int, JsonElement[]> stream in streams)
            {
                Assert.Equal(stream.Value.Length, positions[stream.Key]);
                Assert.Equal(stream.Value.Length, reader.GetPacketCount(stream.Key));
                Assert.Equal(stream.Value.Sum(packet => long.Parse(packet.GetProperty("size").GetString()!)), reader.Tracks[stream.Key].PacketIndex.PayloadBytes);
                for (int index = stream.Value.Length - 1; index >= 0; index--)
                {
                    using MediaPacket packet = await reader.ReadPacketAtAsync(stream.Key, index);
                    VerifyPacket(stream.Value[index], packet, referenceVideoPresentationShift,
                        packet.StreamIndex == 1 && index == 0 ? firstAudioDuration : null);
                }
            }
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Theory]
        [InlineData("fragment-relative.mp4")]
        [InlineData("fragment-absolute.mp4")]
        [InlineData("fragment-implicit.mp4")]
        [InlineData("fragment-signed.mp4")]
        [InlineData("fragment-hybrid.mp4")]
        public async Task CompleteVideoDecodeMatchesIndependentPixelsAndSeekReplaysTheSameFrames(string file) =>
            await Mp4FragmentVideoTestInput.VerifyAsync(file);

        [Fact]
        public async Task FragmentedAudioMatchesIndependentPcmAndEveryRestart()
        {
            byte[] independent = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac", "lc-mono-48.f32le"));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddAudioDecoder(new AacDecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Mp4TestData.PathFor("fragment-aac.m4a")));
            await using AudioSession session = await engine.OpenAudioAsync(source, 0);
            List<(MediaTimestamp Time, byte[] Bytes)> frames = [];
            int offset = 0;
            while (await session.ReadFrameAsync() is AudioFrame frame)
            {
                using (frame)
                {
                    ReadOnlySpan<float> expected = MemoryMarshal.Cast<byte, float>(independent.AsSpan(offset, frame.GetPlane(0).Length));
                    ReadOnlySpan<float> actual = MemoryMarshal.Cast<byte, float>(frame.GetPlane(0).Span);
                    for (int sample = 0; sample < actual.Length; sample++)
                    {
                        Assert.InRange(Math.Abs(actual[sample] - expected[sample]), 0, 2e-6);
                    }
                    frames.Add((frame.Timestamp!.Value, frame.GetPlane(0).ToArray()));
                    offset += frame.GetPlane(0).Length;
                }
            }
            Assert.Equal(independent.Length, offset);
            for (int index = frames.Count - 1; index >= 0; index--)
            {
                await session.SeekAsync(frames[index].Time);
                using AudioFrame frame = Assert.IsType<AudioFrame>(await session.ReadFrameAsync());
                Assert.Equal(frames[index].Time, frame.Timestamp);
                Assert.Equal(frames[index].Bytes, frame.GetPlane(0).ToArray());
            }
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
        }

        [Fact]
        public async Task NoiseSubstitutionRestartsPreserveIndependentBlockEnergyAndExactTimestamps()
        {
            byte[] independent = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac", "lc-pns-48.f32le"));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddAudioDecoder(new AacDecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Mp4TestData.PathFor("fragment-pns.m4a")));
            await using AudioSession session = await engine.OpenAudioAsync(source, 0);
            List<(MediaTimestamp Time, int Offset, int Length)> frames = [];
            int offset = 0;
            while (await session.ReadFrameAsync() is AudioFrame frame)
            {
                using (frame)
                {
                    ReadOnlySpan<byte> pcm = frame.GetPlane(0).Span;
                    _ = AacDecodeTestInput.GetVerifiedNoiseEnergy(pcm, independent.AsSpan(offset, pcm.Length), frame.Format.Channels.Count);
                    frames.Add((frame.Timestamp!.Value, offset, pcm.Length));
                    offset += pcm.Length;
                }
            }
            Assert.Equal(independent.Length, offset);
            for (int index = frames.Count - 1; index >= 0; index--)
            {
                await session.SeekAsync(frames[index].Time);
                using AudioFrame frame = Assert.IsType<AudioFrame>(await session.ReadFrameAsync());
                Assert.Equal(frames[index].Time, frame.Timestamp);
                _ = AacDecodeTestInput.GetVerifiedNoiseEnergy(frame.GetPlane(0).Span,
                    independent.AsSpan(frames[index].Offset, frames[index].Length), frame.Format.Channels.Count);
            }
        }

        private static void VerifyPacket(JsonElement expected, MediaPacket packet, int referenceVideoPresentationShift, int? declaredDuration)
        {
            long presentation = expected.GetProperty("pts").GetInt64();
            if (packet.StreamIndex == 0)
            {
                presentation -= referenceVideoPresentationShift;
            }
            Assert.Equal(presentation, packet.PresentationTime!.Value.Value);
            Assert.Equal(expected.GetProperty("dts").GetInt64(), packet.DecodeTime!.Value.Value);
            Assert.Equal(declaredDuration ?? expected.GetProperty("duration").GetInt64(), packet.Duration!.Value.Value);
            Assert.Equal(expected.GetProperty("data_hash").GetString(), "SHA256:" + Convert.ToHexStringLower(SHA256.HashData(packet.Data.Span)));
            Assert.Equal(expected.GetProperty("flags").GetString()!.StartsWith('K'), packet.IsKeyFrame);
        }
    }
}
