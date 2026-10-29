using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs.Pcm;
using Mediana.Containers.Wave;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    internal static class WaveformReferenceTestInput
    {
        public static readonly string[] Names = ["silence", "impulses", "music", "speech", "fractional"];

        public static async Task<(double MaximumError, double RmsError)> VerifyAsync(string root, string name, int packetSize)
        {
            using JsonDocument document = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "references.json")));
            JsonElement entry = document.RootElement.GetProperty("files").EnumerateArray()
                .Single(value => value.GetProperty("name").GetString() == name);
            byte[] sourceBytes = await File.ReadAllBytesAsync(Path.Combine(root, name + ".wav"));
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + ".s16le"));
            if (Convert.ToHexStringLower(SHA256.HashData(sourceBytes)) != entry.GetProperty("inputSha256").GetString()
                || Convert.ToHexStringLower(SHA256.HashData(reference)) != entry.GetProperty("referenceSha256").GetString())
            {
                throw new InvalidDataException("The waveform source or independent reference hash changed.");
            }
            int sampleRate = entry.GetProperty("sampleRate").GetInt32();
            long count = entry.GetProperty("sourceSamplesPerChannel").GetInt64();
            int expectedSamples = entry.GetProperty("referenceSamples").GetInt32();
            if ((count * 400 + sampleRate - 1) / sampleRate != expectedSamples || reference.Length != expectedSamples * sizeof(short))
            {
                throw new InvalidDataException("The waveform reference count differs from the expected exact duration.");
            }
            double[] gains = entry.GetProperty("channels").GetInt32() switch
            {
                1 => [1],
                2 => [0.5, 0.5],
                _ => throw new InvalidDataException("The waveform reference has an unsupported channel count.")
            };
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new WaveReaderFactory(new WaveReadOptions { SamplesPerPacket = packetSize }))
                .AddAudioDecoder(new PcmDecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".wav")));
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
                mixer ??= new AudioChannelMixer(frame.Format, ["mono"], gains);
                return mixer.Apply(frame, allocator, cancellationToken);
            }
            await using AudioResampler resampler = new(ReadMixedAsync, 400, allocator,
                new AudioResampleOptions { SamplesPerFrame = 73, CutoffRatio = 0.97, BoundaryMode = AudioBoundaryMode.Reflect });
            AudioLevelAccumulator accumulator = new(40);
            int position = 0;
            int levelCount = 0;
            double maximum = 0;
            double squaredError = 0;
            double referenceEnergy = 0;
            void Emit(AudioLevel level)
            {
                int first = checked((int)level.StartSample);
                double energy = 0;
                double peak = 0;
                for (int index = first; index < first + level.SampleCount; index++)
                {
                    double value = BinaryPrimitives.ReadInt16LittleEndian(reference.AsSpan(index * sizeof(short))) / 32768d;
                    energy += value * value;
                    peak = Math.Max(peak, Math.Abs(value));
                }
                double rms = Math.Sqrt(energy / level.SampleCount);
                if (first != levelCount * 40 || level.SampleCount != Math.Min(40, expectedSamples - first)
                    || level.Timestamp != new MediaTimestamp(first, new TimeBase(1, 400))
                    || level.Duration != new MediaDuration(level.SampleCount, new TimeBase(1, 400))
                    || !double.IsFinite(level.RootMeanSquare) || !double.IsFinite(level.Peak)
                    || Math.Abs(level.RootMeanSquare - rms) > 0.0002 + 0.01 * rms || Math.Abs(level.Peak - peak) > 0.002)
                {
                    throw new InvalidDataException($"The waveform levels, timing or final partial window differ from the independent reference in {name}.");
                }
                levelCount++;
            }
            while (true)
            {
                using AudioFrame? frame = await resampler.ReadFrameAsync();
                if (frame is null)
                {
                    break;
                }
                if (frame.Timestamp != new MediaTimestamp(position, new TimeBase(1, 400))
                    || frame.Duration != new MediaDuration(frame.SamplesPerChannel, new TimeBase(1, 400))
                    || position + frame.SamplesPerChannel > expectedSamples)
                {
                    throw new InvalidDataException("The waveform PCM timeline changed.");
                }
                using AudioFrame pcm = AudioSampleConverter.Apply(frame, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, allocator);
                ReadOnlySpan<short> values = MemoryMarshal.Cast<byte, short>(pcm.GetPlane(0).Span);
                for (int index = 0; index < values.Length; index++)
                {
                    double expected = BinaryPrimitives.ReadInt16LittleEndian(reference.AsSpan((position + index) * sizeof(short))) / 32768d;
                    double difference = values[index] / 32768d - expected;
                    maximum = Math.Max(maximum, Math.Abs(difference));
                    squaredError += difference * difference;
                    referenceEnergy += expected * expected;
                }
                accumulator.Process(pcm, Emit);
                position += frame.SamplesPerChannel;
            }
            accumulator.Complete(Emit);
            double errorRms = Math.Sqrt(squaredError / expectedSamples);
            if (position != expectedSamples || levelCount != (expectedSamples + 39) / 40 || maximum > 0.002
                || errorRms > 1.0 / 32768 + 0.01 * Math.Sqrt(referenceEnergy / expectedSamples))
            {
                throw new InvalidDataException($"The waveform PCM differs from the independent reference in {name}: max {maximum}, RMS {errorRms}.");
            }
            await resampler.DisposeAsync();
            await session.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("The waveform reference pipeline retained audio storage.");
            }
            return (maximum, errorRms);
        }
    }
}
