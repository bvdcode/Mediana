// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Dts;
using Mediana.Containers.Matroska;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    internal static class DtsWaveformTestInput
    {
        public static async Task VerifyAsync(string root, string name, int rate, int channels)
        {
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + ".pcm"));
            long inputSamples = reference.Length / (sizeof(float) * channels);
            long expectedSamples = (inputSamples * 400 + rate - 1) / rate;
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory())
                .AddAudioDecoder(new DtsDecoderFactory()).Build();
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".mkv")));
            await using AudioSession session = await engine.OpenAudioAsync(source);
            double[] gains = Enumerable.Repeat(1.0 / channels, channels).ToArray();
            AudioWaveformOptions options = new(400, 40);
            await using AudioWaveformReader reader = new(session.ReadFrameAsync, gains, options, allocator);
            AudioFormat format = new(rate, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved,
                Enumerable.Range(0, channels).Select(index => $"channel-{index}").ToArray());
            int referenceOffset = 0;
            await using AudioWaveformReader expected = new(ReadReferenceAsync, gains, options, allocator);
            long samples = 0;
            while (await reader.ReadLevelAsync() is AudioLevel level)
            {
                AudioLevel comparison = await expected.ReadLevelAsync() ?? throw new InvalidDataException("Missing reference waveform window.");
                if (level.StartSample != samples || level.Timestamp != new MediaTimestamp(samples, new(1, 400)) ||
                    level.SampleCount != Math.Min(40, expectedSamples - samples) ||
                    level.Duration != comparison.Duration || !double.IsFinite(level.RootMeanSquare) || !double.IsFinite(level.Peak) ||
                    Math.Abs(level.RootMeanSquare - comparison.RootMeanSquare) > 0.00004 || Math.Abs(level.Peak - comparison.Peak) > 0.00004)
                {
                    throw new InvalidDataException("DTS waveform timing or levels differ from independent PCM.");
                }
                samples += level.SampleCount;
            }
            if (samples != expectedSamples || await expected.ReadLevelAsync() is not null)
            {
                throw new InvalidDataException("DTS waveform lost its final samples.");
            }
            await session.DisposeAsync();
            if (session.AllocatedBufferBytes != 0 || allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("DTS waveform retained storage after completion.");
            }

            ValueTask<AudioFrame?> ReadReferenceAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (referenceOffset == reference.Length)
                {
                    return ValueTask.FromResult<AudioFrame?>(null);
                }
                int bytes = Math.Min(format.GetBufferSize(137), reference.Length - referenceOffset);
                using WritableMediaBuffer writable = allocator.Allocate(bytes);
                reference.AsSpan(referenceOffset, bytes).CopyTo(writable.Span);
                using MediaBuffer buffer = writable.Publish();
                AudioFrame frame = new(format, bytes / (sizeof(float) * channels), buffer,
                    new(referenceOffset / (sizeof(float) * channels), new(1, rate)));
                referenceOffset += bytes;
                return ValueTask.FromResult<AudioFrame?>(frame);
            }
        }
    }
}
