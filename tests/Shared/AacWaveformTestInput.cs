using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Mediana.Codecs.Aac;
using Mediana.Containers.Mp4;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    internal static class AacWaveformTestInput
    {
        public static async Task VerifyAsync(string root, string name, bool trimToEdit = false)
        {
            using JsonDocument references = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "references.json")));
            JsonElement entry = references.RootElement.GetProperty("fixtures").EnumerateArray()
                .Single(value => value.GetProperty("name").GetString() == name);
            if (entry.GetProperty("pns").GetBoolean())
            {
                throw new ArgumentException("Sample-level waveform comparison requires a reference without noise substitution.", nameof(name));
            }
            int rate = entry.GetProperty("sampleRate").GetInt32();
            int channels = entry.GetProperty("channels").GetInt32();
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + ".f32le"));
            long sourceSamples = reference.Length / (channels * sizeof(float));
            MediaBufferAllocator allocator = new(256 * 1024);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddAudioDecoder(new AacDecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".m4a")));
            Mp4AudioSampleRange? range = null;
            if (trimToEdit)
            {
                await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
                range = Mp4AudioSampleRange.FromEditList(reader.Tracks[0], rate);
                reference = reference.AsSpan(checked((int)range.Value.StartSample * channels * sizeof(float)),
                    checked((int)range.Value.SampleCount * channels * sizeof(float))).ToArray();
                sourceSamples = range.Value.SampleCount;
            }
            await using AudioSession session = await engine.OpenAudioAsync(source, 0, new MediaOpenOptions { BufferBudgetBytes = 128 * 1024 });
            await using AudioTrimmer? trimmer = range is Mp4AudioSampleRange selected
                ? new AudioTrimmer(session.ReadFrameAsync, selected.StartSample, selected.SampleCount, allocator, selected.PresentationTime)
                : null;
            Func<CancellationToken, ValueTask<AudioFrame?>> read = session.ReadFrameAsync;
            if (trimmer is not null)
            {
                read = trimmer.ReadFrameAsync;
            }
            (short[] actual, AudioLevel[] actualLevels) = await ProcessAsync(read, allocator, channels, 7);
            string[] channelNames = channels switch
            {
                1 => ["front-center"],
                2 => ["front-left", "front-right"],
                _ => throw new InvalidDataException("The waveform fixture channel count is unsupported.")
            };
            AudioSignalSource independent = new(allocator, rate, sourceSamples,
                (index, channel) => BinaryPrimitives.ReadSingleLittleEndian(reference.AsSpan(checked((int)(index * channels + channel) * sizeof(float)), sizeof(float))),
                [37, 101], channels: channelNames)
            {
                Timestamp = index => new MediaTimestamp(index, new TimeBase(1, rate))
            };
            (short[] expected, AudioLevel[] expectedLevels) = await ProcessAsync(independent.ReadAsync, allocator, channels, 31);
            long outputSamples = (sourceSamples * 400 + rate - 1) / rate;
            if (actual.LongLength != outputSamples || actual.Length != expected.Length || actualLevels.Length != expectedLevels.Length
                || actualLevels.Sum(level => level.SampleCount) != outputSamples)
            {
                throw new InvalidDataException("AAC waveform changed the reference sample count or final partial window.");
            }
            for (int index = 0; index < actual.Length; index++)
            {
                if (Math.Abs(actual[index] - expected[index]) > 1)
                {
                    throw new InvalidDataException($"AAC mono 400 Hz PCM differs from independent decoded input at sample {index}.");
                }
            }
            for (int index = 0; index < actualLevels.Length; index++)
            {
                VerifyLevel(actualLevels[index], expectedLevels[index], index);
            }
            await session.DisposeAsync();
            await using AudioSession readerSession = await engine.OpenAudioAsync(source, 0, new MediaOpenOptions { BufferBudgetBytes = 128 * 1024 });
            await using AudioTrimmer? readerTrimmer = range is Mp4AudioSampleRange readerRange
                ? new AudioTrimmer(readerSession.ReadFrameAsync, readerRange.StartSample, readerRange.SampleCount, allocator, readerRange.PresentationTime)
                : null;
            Func<CancellationToken, ValueTask<AudioFrame?>> readSelected = readerSession.ReadFrameAsync;
            if (readerTrimmer is not null)
            {
                readSelected = readerTrimmer.ReadFrameAsync;
            }
            await using AudioWaveformReader waveform = new(readSelected, GetGains(channels), new AudioWaveformOptions(400, 16)
            {
                SampleFormat = AudioSampleFormat.Signed16,
                Resampling = new AudioResampleOptions { SamplesPerFrame = 3 }
            }, allocator);
            int levelCount = 0;
            while (await waveform.ReadLevelAsync() is AudioLevel level)
            {
                if (levelCount >= expectedLevels.Length)
                {
                    throw new InvalidDataException("The AAC waveform reader produced extra windows.");
                }
                VerifyLevel(level, expectedLevels[levelCount], levelCount);
                levelCount++;
            }
            if (levelCount != expectedLevels.Length)
            {
                throw new InvalidDataException("The AAC waveform reader omitted a window.");
            }
            await waveform.DisposeAsync();
            await readerSession.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0 || readerSession.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("The AAC waveform pipeline retained audio storage.");
            }
        }

        private static async Task<(short[] Samples, AudioLevel[] Levels)> ProcessAsync(
            Func<CancellationToken, ValueTask<AudioFrame?>> read, MediaBufferAllocator allocator, int channels, int blockSize)
        {
            double[] gains = GetGains(channels);
            AudioChannelMixer? mixer = null;
            async ValueTask<AudioFrame?> ReadMixedAsync(CancellationToken cancellationToken)
            {
                using AudioFrame? input = await read(cancellationToken);
                if (input is null)
                {
                    return null;
                }
                mixer ??= new AudioChannelMixer(input.Format, ["mono"], gains);
                return mixer.Apply(input, allocator, cancellationToken);
            }
            await using AudioResampler resampler = new(ReadMixedAsync, 400, allocator, new AudioResampleOptions { SamplesPerFrame = blockSize });
            List<short> samples = [];
            List<AudioLevel> levels = [];
            AudioLevelAccumulator accumulator = new(16);
            while (true)
            {
                using AudioFrame? frame = await resampler.ReadFrameAsync();
                if (frame is null)
                {
                    break;
                }
                using AudioFrame pcm = AudioSampleConverter.Apply(frame, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, allocator);
                samples.AddRange(MemoryMarshal.Cast<byte, short>(pcm.GetPlane(0).Span).ToArray());
                accumulator.Process(pcm, levels.Add);
            }
            accumulator.Complete(levels.Add);
            return (samples.ToArray(), levels.ToArray());
        }

        private static double[] GetGains(int channels) => channels switch
        {
            1 => [1],
            2 => [0.5, 0.5],
            _ => throw new InvalidDataException("The waveform fixture channel count is unsupported.")
        };

        private static void VerifyLevel(AudioLevel level, AudioLevel expected, int index)
        {
            if (level.StartSample != index * 16 || level.SampleCount != expected.SampleCount
                || level.Timestamp != new MediaTimestamp(index * 16, new TimeBase(1, 400))
                || level.Duration != new MediaDuration(level.SampleCount, new TimeBase(1, 400))
                || Math.Abs(level.RootMeanSquare - expected.RootMeanSquare) > 1.0 / 32768
                || Math.Abs(level.Peak - expected.Peak) > 1.0 / 32768)
            {
                throw new InvalidDataException("AAC waveform RMS, peak or timing differs from independent decoded input.");
            }
        }
    }
}
