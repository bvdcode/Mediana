// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text.Json;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Processing.Audio;

namespace Mediana.Cli
{
    internal static class AudioWaveformCommand
    {
        private const int BufferBudgetBytes = 1024 * 1024;
        private const int WindowsPerFlush = 64;
        private const double CutoffRatio = 0.97;

        public static async Task<int> RunAsync(string inputPath, string outputPath, int streamIndex,
            int sampleRate, int samplesPerWindow, double[] gains, CancellationToken cancellationToken = default)
        {
            MediaEngine engine = AudioEngineFactory.Create();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(inputPath));
            await using AudioSession session = await engine.OpenAudioAsync(source, streamIndex,
                new MediaOpenOptions { BufferBudgetBytes = BufferBudgetBytes }, cancellationToken: cancellationToken);
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            await using AudioWaveformReader reader = new(session.ReadFrameAsync, gains,
                new AudioWaveformOptions(sampleRate, samplesPerWindow)
                {
                    SampleFormat = AudioSampleFormat.Signed16,
                    Resampling = new AudioResampleOptions { CutoffRatio = CutoffRatio, BoundaryMode = AudioBoundaryMode.Reflect }
                }, allocator);
            long samples = 0;
            long windows = 0;
            await CommandOutputFile.WriteAsync(outputPath, async output =>
            {
                using Utf8JsonWriter json = new(output, new JsonWriterOptions { Indented = true });
                json.WriteStartObject();
                json.WriteNumber("sampleRate", sampleRate);
                json.WriteNumber("samplesPerWindow", samplesPerWindow);
                json.WriteString("sampleFormat", "signed16");
                json.WriteNumber("cutoffRatio", CutoffRatio);
                json.WriteString("boundaryMode", "reflect");
                json.WriteStartArray("channelGains");
                foreach (double gain in gains)
                {
                    json.WriteNumberValue(gain);
                }
                json.WriteEndArray();
                json.WriteStartArray("windows");
                while (await reader.ReadLevelAsync(cancellationToken) is AudioLevel level)
                {
                    WriteLevel(json, level);
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
            }, cancellationToken: cancellationToken);
            await reader.DisposeAsync();
            await session.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("Audio waveform buffers were not released.");
            }
            await Console.Out.WriteLineAsync($"Waveform: {samples} mono samples at {sampleRate} Hz; {windows} RMS/peak windows. Live buffer bytes: 0.");
            return 0;
        }

        private static void WriteLevel(Utf8JsonWriter json, AudioLevel level)
        {
            json.WriteStartObject();
            json.WriteNumber("startSample", level.StartSample);
            json.WriteNumber("sampleCount", level.SampleCount);
            if (level.Timestamp is MediaTimestamp timestamp)
            {
                WriteTime(json, "timestamp", timestamp.Value, timestamp.TimeBase);
            }
            else
            {
                json.WriteNull("timestamp");
            }
            WriteTime(json, "duration", level.Duration.Value, level.Duration.TimeBase);
            json.WriteNumber("rms", level.RootMeanSquare);
            json.WriteNumber("peak", level.Peak);
            json.WriteEndObject();
        }

        private static void WriteTime(Utf8JsonWriter json, string name, long value, TimeBase timeBase)
        {
            json.WriteStartObject(name);
            json.WriteNumber("value", value);
            json.WriteNumber("numerator", timeBase.Numerator);
            json.WriteNumber("denominator", timeBase.Denominator);
            json.WriteEndObject();
        }
    }
}
