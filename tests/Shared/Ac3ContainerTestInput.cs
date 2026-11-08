// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Ac3;
using Mediana.Containers.Ac3;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    internal static class Ac3ContainerTestInput
    {
        public static async Task VerifyAsync(string containerName, string elementaryName, bool enhanced, int channels)
        {
            byte[] encoded = await ReadAsync(containerName);
            byte[] raw = await ReadAsync(elementaryName);
            MediaEngine engine = Engine();
            await using EbmlElementTestSource source = new(encoded) { MaximumRead = 7 };
            await using EbmlElementTestSource original = new(raw) { MaximumRead = 3 };
            MediaInfo info = await engine.ProbeAsync(source);
            Require(info.StreamMetadata[0] is AudioStreamMetadata { SampleRate: 48000 } metadata && metadata.ChannelCount == channels);
            Require(info.Streams[0].Format == (enhanced ? StreamFormats.Eac3Mp4 : StreamFormats.Ac3Mp4));
            Require(info.Streams[0].Configuration.Length == (enhanced ? 6 : 3));
            await using (AudioSession audio = await engine.OpenAudioAsync(source))
            await using (AudioSession reference = await engine.OpenAudioAsync(original))
            {
                int count = 0, samples = 0;
                while (await audio.ReadFrameAsync() is AudioFrame actual)
                {
                    using (actual)
                    using (AudioFrame expected = (await reference.ReadFrameAsync())!)
                    {
                        Require(expected.GetPlane(0).Span.SequenceEqual(actual.GetPlane(0).Span));
                        Require(expected.Timestamp == actual.Timestamp && actual.SamplesPerChannel == 1536);
                        Require(actual.Format.Channels.Count == channels);
                        samples += actual.SamplesPerChannel; count++;
                    }
                }
                Require(count == 4 && samples == 6144 && await reference.ReadFrameAsync() is null);
                await audio.SeekAsync(new(4800, new(1, 48000)));
                using AudioFrame tail = (await audio.ReadFrameAsync())!;
                Require(tail.Timestamp == new MediaTimestamp(4608, new(1, 48000)) && tail.SamplesPerChannel == 1536);
                Require(await audio.ReadFrameAsync() is null);
                tail.Dispose();
                await audio.DisposeAsync(); await reference.DisposeAsync();
                Require(audio.AllocatedBufferBytes == 0 && reference.AllocatedBufferBytes == 0);
            }
            Require(!source.IsDisposed && !original.IsDisposed);
        }

        public static async Task VerifyBundledAndSeekAsync()
        {
            byte[] encoded = await ReadAsync("one-block-eac3.mp4");
            byte[] frame = await ReadAsync("enhanced-one.eac3");
            byte[] raw = Enumerable.Range(0, 12).SelectMany(_ => frame).ToArray();
            await using EbmlElementTestSource source = new(encoded);
            await using EbmlElementTestSource original = new(raw);
            MediaBufferAllocator allocator = new(1048576);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                Require(reader.GetPacketCount(0) == 2);
                using MediaPacket packet = await reader.ReadPacketAtAsync(0, 1);
                Require(packet.Data.Length == frame.Length * 6 && packet.PresentationTime == new MediaTimestamp(1536, new(1, 48000)));
            }
            Require(allocator.AllocatedBytes == 0);
            MediaEngine engine = Engine();
            byte[] expected = new byte[3072 * sizeof(float)];
            await using (AudioSession reference = await engine.OpenAudioAsync(original))
            await using (AudioSession audio = await engine.OpenAudioAsync(source))
            {
                int offset = 0;
                while (await reference.ReadFrameAsync() is AudioFrame pcm)
                {
                    using (pcm)
                    {
                        Require(pcm.SamplesPerChannel == 256);
                        pcm.GetPlane(0).Span.CopyTo(expected.AsSpan(offset)); offset += pcm.GetPlane(0).Length;
                    }
                }
                Require(offset == expected.Length); offset = 0;
                while (await audio.ReadFrameAsync() is AudioFrame pcm)
                {
                    using (pcm)
                    {
                        Require(pcm.SamplesPerChannel == 1536);
                        Require(pcm.GetPlane(0).Span.SequenceEqual(expected.AsSpan(offset, pcm.GetPlane(0).Length)));
                        offset += pcm.GetPlane(0).Length;
                    }
                }
                Require(offset == expected.Length);
                await audio.SeekAsync(new(1600, new(1, 48000)));
                using AudioFrame tail = (await audio.ReadFrameAsync())!;
                Require(tail.Timestamp == new MediaTimestamp(1536, new(1, 48000)));
                Require(tail.GetPlane(0).Span.SequenceEqual(expected.AsSpan(1536 * sizeof(float))));
                await reference.SeekAsync(new(1600, new(1, 48000)));
                using AudioFrame rawTail = (await reference.ReadFrameAsync())!;
                Require(rawTail.Timestamp == tail.Timestamp && rawTail.SamplesPerChannel == 256);
                Require(rawTail.GetPlane(0).Span.SequenceEqual(expected.AsSpan(1536 * sizeof(float), 256 * sizeof(float))));
                tail.Dispose(); rawTail.Dispose();
                await audio.DisposeAsync(); await reference.DisposeAsync();
                Require(audio.AllocatedBufferBytes == 0 && reference.AllocatedBufferBytes == 0);
            }
        }

        public static async Task VerifyTransientAsync()
        {
            byte[] raw = await ReadAsync("transient.eac3");
            byte[] encoded = await ReadAsync("transient.mp4");
            MediaEngine engine = Engine();
            byte[] expected = new byte[15360 * sizeof(float)];
            await using EbmlElementTestSource original = new(raw);
            await using EbmlElementTestSource source = new(encoded);
            await using (AudioSession audio = await engine.OpenAudioAsync(original))
            {
                int offset = 0;
                while (await audio.ReadFrameAsync() is AudioFrame pcm)
                {
                    using (pcm)
                    {
                        Require(pcm.Timestamp == new MediaTimestamp(offset / sizeof(float), new(1, 48000)));
                        pcm.GetPlane(0).Span.CopyTo(expected.AsSpan(offset)); offset += pcm.GetPlane(0).Length;
                    }
                }
                Require(offset == expected.Length);
            }
            await using (AudioSession audio = await engine.OpenAudioAsync(source))
            {
                int offset = 0;
                while (await audio.ReadFrameAsync() is AudioFrame pcm)
                {
                    using (pcm)
                    {
                        Require(pcm.GetPlane(0).Span.SequenceEqual(expected.AsSpan(offset, pcm.GetPlane(0).Length)));
                        offset += pcm.GetPlane(0).Length;
                    }
                }
                Require(offset == expected.Length);
                await audio.SeekAsync(new(11000, new(1, 48000)));
                using AudioFrame tail = (await audio.ReadFrameAsync())!;
                Require(tail.Timestamp == new MediaTimestamp(10752, new(1, 48000)));
                Require(tail.GetPlane(0).Span.SequenceEqual(expected.AsSpan(10752 * sizeof(float), 1536 * sizeof(float))));
                tail.Dispose(); await audio.DisposeAsync(); Require(audio.AllocatedBufferBytes == 0);
            }
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using (HlsAacPcmSource pcm = await HlsAacPcmSource.OpenAsync(engine, source, 0, null, allocator))
            {
                int offset = 0;
                while (await pcm.ReadFrameAsync(default) is AudioFrame frame)
                {
                    using (frame)
                    {
                        Require(frame.GetPlane(0).Span.SequenceEqual(expected.AsSpan(offset, frame.GetPlane(0).Length)));
                        offset += frame.GetPlane(0).Length;
                    }
                }
                Require(offset == expected.Length);
            }
            Require(allocator.AllocatedBytes == 0);
            await using (HlsAacAudioSource audio = await HlsAacAudioSource.OpenAsync(engine, source, 0, [64000], null, allocator, default))
            {
                await audio.WriteBeforeAsync(null, [], default);
                Require(audio.InputSamplesPerChannel == 15360 && audio.PacketsWritten == 16);
                Require(audio.PrimingSamples == 1024 && audio.PaddingSamples == 0);
            }
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed && !original.IsDisposed);
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("The AC-3 container metadata, PCM, clock or resource lifetime is incorrect."); }
        }
        private static MediaEngine Engine() => new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
            .AddContainer(new Ac3ReaderFactory()).AddAudioDecoder(new Ac3DecoderFactory(false))
            .AddAudioDecoder(new Eac3DecoderFactory(false)).AddStreamMetadataReader(new Ac3StreamMetadataReader())
            .AddStreamMetadataReader(new Eac3StreamMetadataReader()).Build();
        private static Task<byte[]> ReadAsync(string name) => File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Ac3", name));
    }
}
