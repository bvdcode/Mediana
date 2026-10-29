// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Pcm;
using Mediana.Containers.Wave;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class WavePcmTestInput
    {
        public static readonly string[] Names = ["u8-mono", "s16-stereo", "s24-stereo", "s32-stereo", "f32-stereo", "f64-stereo", "s24-surround"];

        public static async Task VerifyAsync(string root, string name)
        {
            (int rate, AudioSampleFormat format, string[] channels) = name switch
            {
                "u8-mono" => (8000, AudioSampleFormat.Unsigned8, new[] { "mono" }),
                "s16-stereo" => (44100, AudioSampleFormat.Signed16, new[] { "front-left", "front-right" }),
                "s24-stereo" or "s32-stereo" => (48000, AudioSampleFormat.Signed32, new[] { "front-left", "front-right" }),
                "f32-stereo" => (44100, AudioSampleFormat.Float32, new[] { "front-left", "front-right" }),
                "f64-stereo" => (48000, AudioSampleFormat.Float64, new[] { "front-left", "front-right" }),
                "s24-surround" => (48000, AudioSampleFormat.Signed32, new[] { "front-left", "front-right", "front-center", "low-frequency", "back-left", "back-right" }),
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };
            AudioFormat expectedFormat = new(rate, format, AudioStorageLayout.Interleaved, channels);
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + ".pcm"));
            if (!BitConverter.IsLittleEndian)
            {
                for (int index = 0; index < expected.Length; index += expectedFormat.BytesPerSample)
                {
                    Array.Reverse(expected, index, expectedFormat.BytesPerSample);
                }
            }
            int sampleBytes = expectedFormat.BytesPerSample * channels.Length;
            int samples = expected.Length / sampleBytes;
            MediaBufferAllocator metadataAllocator = new(16384);
            await using (StreamByteSource input = new(File.OpenRead(Path.Combine(root, name + ".wav"))))
            await using (WaveReader reader = await WaveReader.OpenAsync(input, metadataAllocator))
            {
                if (reader.SamplesPerChannel != samples || reader.Duration != new MediaDuration(samples, new TimeBase(1, rate))
                    || reader.Configuration.Format.SampleFormat != format || !reader.Configuration.Format.Channels.SequenceEqual(channels))
                {
                    throw new InvalidDataException("WAVE metadata does not match the independent PCM reference.");
                }
            }
            if (metadataAllocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("WAVE metadata reading retained buffer storage.");
            }
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new WaveReaderFactory(new WaveReadOptions { SamplesPerPacket = 127 }))
                .AddAudioDecoder(new PcmDecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".wav")));
            await using AudioSession session = await engine.OpenAudioAsync(source, 0, new MediaOpenOptions { BufferBudgetBytes = 16384 });
            AudioFrame? retained = null;
            int position = 0;
            try
            {
                while (true)
                {
                    using AudioFrame? frame = await session.ReadFrameAsync();
                    if (frame is null)
                    {
                        break;
                    }
                    int bytes = frame.SamplesPerChannel * sampleBytes;
                    if (frame.Format.SampleFormat != format || frame.Format.SampleRate != rate || !frame.Format.Channels.SequenceEqual(channels)
                        || frame.Format.Layout != AudioStorageLayout.Interleaved || frame.SamplesPerChannel > 127
                        || !frame.GetPlane(0).Span.SequenceEqual(expected.AsSpan(position * sampleBytes, bytes))
                        || frame.Timestamp != new MediaTimestamp(position, new TimeBase(1, rate))
                        || frame.Duration != new MediaDuration(frame.SamplesPerChannel, new TimeBase(1, rate)))
                    {
                        throw new InvalidDataException($"WAVE PCM mismatch at sample {position} in {name}.");
                    }
                    position += frame.SamplesPerChannel;
                    retained?.Dispose();
                    retained = frame.Retain();
                }
                if (position != samples || await session.ReadFrameAsync() is not null)
                {
                    throw new InvalidDataException("WAVE PCM changed its sample count or end of stream.");
                }
                await session.DisposeAsync();
                if (retained is null || session.AllocatedBufferBytes != retained.GetPlane(0).Length
                    || !retained.GetPlane(0).Span.SequenceEqual(expected.AsSpan(expected.Length - retained.GetPlane(0).Length)))
                {
                    throw new InvalidOperationException("PCM frame ownership did not survive audio session disposal.");
                }
            }
            finally
            {
                retained?.Dispose();
            }
            if (session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("The public audio session retained PCM storage.");
            }
        }
    }
}
