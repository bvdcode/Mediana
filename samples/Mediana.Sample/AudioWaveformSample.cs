// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text.Json;
using Mediana.Codecs.Aac;
using Mediana.Codecs.Flac;
using Mediana.Codecs.Mp3;
using Mediana.Codecs.Pcm;
using Mediana.Containers.Flac;
using Mediana.Containers.Mp3;
using Mediana.Containers.Mp4;
using Mediana.Containers.Wave;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;
using Mediana.Processing.Audio;

namespace Mediana.Sample
{
    internal static class AudioWaveformSample
    {
        private const int SampleRate = 400;
        private const int SamplesPerWindow = 40;
        private const int DecoderBudgetBytes = 1024 * 1024;
        private const int ProcessingBudgetBytes = 256 * 1024;
        private const int WindowsPerFlush = 64;

        public static async Task<int> RunAsync(string input, CancellationToken cancellationToken = default)
        {
            MediaEngine engine = CreateEngine();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            await WriteAsync(engine, source, Console.OpenStandardOutput(), cancellationToken);
            return 0;
        }

        internal static MediaEngine CreateEngine() => CreateBuilder().Build();

        internal static MediaEngineBuilder CreateBuilder() => new MediaEngineBuilder()
            .AddContainer(new WaveReaderFactory())
            .AddContainer(new Mp3ReaderFactory())
            .AddContainer(new FlacReaderFactory())
            .AddContainer(new Mp4ReaderFactory())
            .AddAudioDecoder(new PcmDecoderFactory())
            .AddAudioDecoder(new Mp3DecoderFactory())
            .AddAudioDecoder(new FlacDecoderFactory())
            .AddAudioDecoder(new AacDecoderFactory());

        internal static async Task WriteAsync(MediaEngine engine, IByteSource source, Stream destination,
            CancellationToken cancellationToken = default)
        {
            AudioSession session = await engine.OpenAudioAsync(source,
                new MediaOpenOptions { BufferBudgetBytes = DecoderBudgetBytes }, cancellationToken);
            await WriteAsync(session, destination, cancellationToken);
        }

        /// <summary>Consumes and disposes the supplied audio session; the destination remains borrowed.</summary>
        internal static async Task WriteAsync(AudioSession session, Stream destination,
            CancellationToken cancellationToken = default)
        {
            await using AudioSession owner = session;
            MediaBufferAllocator allocator = new(ProcessingBudgetBytes);
            AudioFrame? pending = await session.ReadFrameAsync(cancellationToken)
                ?? throw new InvalidDataException("The waveform requires at least one decoded audio frame.");
            try
            {
                AudioChannelMixer mixer = AudioDownmix.CreateMono(pending.Format);
                await using AudioWaveformReader reader = new(ReadAsync, mixer,
                    new AudioWaveformOptions(SampleRate, SamplesPerWindow)
                    {
                        SampleFormat = AudioSampleFormat.Signed16,
                        Resampling = new AudioResampleOptions { CutoffRatio = 0.97, BoundaryMode = AudioBoundaryMode.Reflect }
                    }, allocator);
                using Utf8JsonWriter json = new(destination);
                json.WriteStartObject();
                json.WriteNumber("sampleRate", SampleRate);
                json.WriteNumber("samplesPerWindow", SamplesPerWindow);
                json.WriteStartArray("windows");
                long samples = 0;
                long windows = 0;
                while (await reader.ReadLevelAsync(cancellationToken) is AudioLevel level)
                {
                    json.WriteStartObject();
                    json.WriteNumber("startSample", level.StartSample);
                    json.WriteNumber("sampleCount", level.SampleCount);
                    json.WriteNumber("rms", level.RootMeanSquare);
                    json.WriteNumber("peak", level.Peak);
                    json.WriteEndObject();
                    samples = checked(samples + level.SampleCount);
                    windows = checked(windows + 1);
                    if (windows % WindowsPerFlush == 0)
                    {
                        await json.FlushAsync(cancellationToken);
                    }
                }
                json.WriteEndArray();
                json.WriteNumber("sampleCount", samples);
                json.WriteNumber("windowCount", windows);
                json.WriteEndObject();
                await json.FlushAsync(cancellationToken);
                await reader.DisposeAsync();
            }
            finally
            {
                pending?.Dispose();
            }
            await session.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("The waveform retained audio buffers.");
            }

            ValueTask<AudioFrame?> ReadAsync(CancellationToken token)
            {
                if (pending is not null)
                {
                    AudioFrame first = pending;
                    pending = null;
                    return ValueTask.FromResult<AudioFrame?>(first);
                }
                return session.ReadFrameAsync(token);
            }
        }
    }
}
