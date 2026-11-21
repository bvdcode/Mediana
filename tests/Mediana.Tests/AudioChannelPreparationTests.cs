// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    public class AudioChannelPreparationTests
    {
        [Theory]
        [InlineData(AudioSampleFormat.Unsigned8)]
        [InlineData(AudioSampleFormat.Signed16)]
        [InlineData(AudioSampleFormat.Signed32)]
        [InlineData(AudioSampleFormat.Float32)]
        [InlineData(AudioSampleFormat.Float64)]
        public void PermutationPreservesSampleBitsAndUsesOnlyOneOutputBuffer(AudioSampleFormat sampleFormat)
        {
            foreach (AudioStorageLayout sourceLayout in new[] { AudioStorageLayout.Interleaved, AudioStorageLayout.Planar })
            foreach (AudioStorageLayout destinationLayout in new[] { AudioStorageLayout.Interleaved, AudioStorageLayout.Planar })
            {
                const int Count = 257;
                AudioFormat sourceFormat = new(48000, sampleFormat, sourceLayout, "front-center", "front-left", "front-right");
                AudioFormat destinationFormat = new(48000, sampleFormat, destinationLayout, "front-left", "front-right", "front-center");
                MediaBufferAllocator source = new(sourceFormat.GetBufferSize(Count));
                MediaBufferAllocator destination = new(destinationFormat.GetBufferSize(Count));
                using AudioFrame input = AudioSamplePreparationTestInput.CreateFrame(sourceFormat, Count, source);
                AudioFramePreparation preparation = new(sourceFormat, destinationFormat);
                using AudioFrame output = preparation.PrepareOrRetain(input, destination);
                for (int sample = 0; sample < Count; sample++)
                for (int channel = 0; channel < 3; channel++)
                {
                    int from = (channel + 1) % 3;
                    Assert.Equal(Sample(input, sample, from).ToArray(), Sample(output, sample, channel).ToArray());
                }
                Assert.Equal(input.Timestamp, output.Timestamp);
                Assert.Equal(input.Duration, output.Duration);
                Assert.Equal(destinationFormat.GetBufferSize(Count), destination.AllocatedBytes);
                input.Dispose();
                Assert.Equal(0, source.AllocatedBytes);
                output.Dispose();
                Assert.Equal(0, destination.AllocatedBytes);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void WaveRetainsUnassignedChannelOrderForEitherOrdinalConvention(int first)
        {
            AudioFormat input = new(48000, AudioSampleFormat.Signed16, AudioStorageLayout.Planar,
                Enumerable.Range(first, 6).Select(index => $"discrete-{index}").ToArray());
            AudioFormat output = PcmConfiguration.SelectWaveFormat(input);
            Assert.Equal(input.Channels, output.Channels);
            Assert.Equal(AudioStorageLayout.Interleaved, output.Layout);
            PcmConfiguration parsed = PcmConfiguration.ReadWaveFormat(PcmConfiguration.WriteWaveFormat(output), out string? reason)!;
            Assert.Null(reason);
            Assert.Equal(6, parsed.Format.Channels.Count);
        }

        [Fact]
        public void PreparationRejectsMixingResamplingCancellationAndMissingStorage()
        {
            AudioFormat input = new(48000, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, "left", "right");
            Assert.Throws<ArgumentException>(() => new AudioFramePreparation(input,
                new(44100, input.SampleFormat, input.Layout, "left", "right")));
            Assert.Throws<ArgumentException>(() => new AudioFramePreparation(input,
                new(48000, input.SampleFormat, input.Layout, "left", "center")));
            AudioFramePreparation preparation = new(input, new(48000, AudioSampleFormat.Signed16, AudioStorageLayout.Planar, "right", "left"));
            MediaBufferAllocator source = new(2048), destination = new(1);
            using AudioFrame frame = AudioSamplePreparationTestInput.CreateFrame(input, 32, source);
            Assert.Throws<OperationCanceledException>(() => preparation.PrepareOrRetain(frame, destination, new CancellationToken(true)));
            Assert.Throws<MediaMemoryLimitException>(() => preparation.PrepareOrRetain(frame, destination));
            Assert.Equal(0, destination.AllocatedBytes);
        }

        private static ReadOnlySpan<byte> Sample(AudioFrame frame, int sample, int channel)
        {
            int plane = frame.Format.Layout switch
            {
                AudioStorageLayout.Interleaved => 0,
                AudioStorageLayout.Planar => channel,
                _ => throw new ArgumentOutOfRangeException(nameof(frame))
            };
            int offset = frame.Format.Layout switch
            {
                AudioStorageLayout.Interleaved => sample * frame.Format.Channels.Count + channel,
                AudioStorageLayout.Planar => sample,
                _ => throw new ArgumentOutOfRangeException(nameof(frame))
            };
            return frame.GetPlane(plane).Span.Slice(offset * frame.Format.BytesPerSample, frame.Format.BytesPerSample);
        }
    }
}
