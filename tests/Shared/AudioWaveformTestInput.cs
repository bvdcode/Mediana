using Mediana.Codecs.Pcm;
using Mediana.Containers.Wave;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    internal static class AudioWaveformTestInput
    {
        public static async Task VerifyWaveAsync(string root)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new WaveReaderFactory(new WaveReadOptions { SamplesPerPacket = 127 }))
                .AddAudioDecoder(new PcmDecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, "s16-stereo.wav")));
            await using AudioSession session = await engine.OpenAudioAsync(source, 0, new MediaOpenOptions { BufferBudgetBytes = 16384 });
            MediaBufferAllocator allocator = new(256 * 1024);
            AudioChannelMixer? mixer = null;
            async ValueTask<AudioFrame?> ReadMixedAsync(CancellationToken cancellationToken)
            {
                using AudioFrame? frame = await session.ReadFrameAsync(cancellationToken);
                if (frame is null)
                {
                    return null;
                }
                mixer ??= new AudioChannelMixer(frame.Format, ["mono"], [0.5, 0.5]);
                return mixer.Apply(frame, allocator, cancellationToken);
            }
            await using AudioResampler resampler = new(ReadMixedAsync, 400, allocator, new AudioResampleOptions { SamplesPerFrame = 7 });
            AudioLevelAccumulator accumulator = new(16);
            int count = 0;
            long samples = 0;
            void Emit(AudioLevel level)
            {
                if (level.StartSample != samples || level.Timestamp != new MediaTimestamp(samples, new TimeBase(1, 400))
                    || level.Duration != new MediaDuration(level.SampleCount, new TimeBase(1, 400))
                    || !double.IsFinite(level.RootMeanSquare) || level.Peak > 1 || level.RootMeanSquare > level.Peak)
                {
                    throw new InvalidDataException("The real WAVE waveform contains invalid levels or timing.");
                }
                count++;
                samples += level.SampleCount;
            }
            while (true)
            {
                using AudioFrame? frame = await resampler.ReadFrameAsync();
                if (frame is null)
                {
                    break;
                }
                using AudioFrame pcm = AudioSampleConverter.Apply(frame, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, allocator);
                accumulator.Process(pcm, Emit);
            }
            accumulator.Complete(Emit);
            await resampler.DisposeAsync();
            await session.DisposeAsync();
            if (count != 4 || samples != 55 || allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidDataException("The real WAVE waveform lost samples or retained buffer storage.");
            }
        }

        public static async Task VerifyAsync()
        {
            const int rate = 48000;
            MediaBufferAllocator allocator = new(1024 * 1024);
            AudioSignalSource source = new(allocator, rate, rate * 2 + 17,
                (index, channel) => channel switch
                {
                    0 => 0.5 * Math.Sin(2 * Math.PI * 50 * index / rate),
                    1 => 0.25 * Math.Cos(2 * Math.PI * 90 * index / rate),
                    _ => throw new ArgumentOutOfRangeException(nameof(channel))
                }, [37, 101], AudioStorageLayout.Planar, ["front-left", "front-right"]);
            AudioChannelMixer mixer = new(source.Format, ["mono"], [0.5, 0.5]);
            async ValueTask<AudioFrame?> ReadMixedAsync(CancellationToken cancellationToken)
            {
                using AudioFrame? input = await source.ReadAsync(cancellationToken);
                if (input is null)
                {
                    return null;
                }
                return mixer.Apply(input, allocator, cancellationToken);
            }
            await using AudioResampler resampler = new(ReadMixedAsync, 400, allocator, new AudioResampleOptions { SamplesPerFrame = 31 });
            AudioLevelAccumulator accumulator = new(40);
            List<AudioLevel> levels = [];
            while (true)
            {
                using AudioFrame? frame = await resampler.ReadFrameAsync();
                if (frame is null)
                {
                    break;
                }
                using AudioFrame pcm = AudioSampleConverter.Apply(frame, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, allocator);
                accumulator.Process(pcm, levels.Add);
            }
            accumulator.Complete(levels.Add);
            accumulator.Complete(levels.Add);
            double expectedRms = Math.Sqrt((0.25 * 0.25 + 0.125 * 0.125) / 2);
            if (levels.Count != 21 || levels[^1].SampleCount != 1)
            {
                throw new InvalidDataException("Streaming RMS lost the final partial window.");
            }
            for (int index = 0; index < levels.Count; index++)
            {
                AudioLevel level = levels[index];
                if (level.StartSample != index * 40 || level.Timestamp != new MediaTimestamp(index * 4800 - 7, new TimeBase(1, rate))
                    || level.Duration != new MediaDuration(level.SampleCount, new TimeBase(1, 400))
                    || !double.IsFinite(level.RootMeanSquare) || level.Peak < level.RootMeanSquare)
                {
                    throw new InvalidDataException("Streaming RMS metadata or level is incorrect.");
                }
                if (index > 0 && index < 19 && Math.Abs(level.RootMeanSquare - expectedRms) > 0.00005)
                {
                    throw new InvalidDataException("The resampled and quantized waveform differs from independent tone energy.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("The waveform pipeline retained PCM buffers.");
            }
        }
    }
}
