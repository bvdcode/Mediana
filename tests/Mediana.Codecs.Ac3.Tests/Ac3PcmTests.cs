// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs.Ac3;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;
using Xunit.Abstractions;

namespace Mediana.Codecs.Ac3.Tests
{
    public class Ac3PcmTests(ITestOutputHelper output)
    {
        [Theory]
        [InlineData("mono.ac3", "mono.pcm", 48000, 1, 60)]
        [InlineData("stereo.eac3", "stereo.pcm", 48000, 2, 60)]
        [InlineData("stereo-44100.ac3", "stereo-44100.pcm", 44100, 2, 60)]
        [InlineData("surround-32000.ac3", "surround-32000.pcm", 32000, 6, 60)]
        [InlineData("surround.eac3", "surround.pcm", 48000, 6, 60)]
        [InlineData("coupled.ac3", "coupled.pcm", 48000, 2, 60)]
        [InlineData("switched.ac3", "switched.pcm", 48000, 1, 135)]
        [InlineData("enhanced-switched.eac3", "enhanced-switched.pcm", 48000, 1, 135)]
        [InlineData("enhanced-one.eac3", "enhanced-one.pcm", 48000, 1, 135)]
        [InlineData("enhanced-two.eac3", "enhanced-two.pcm", 48000, 1, 135)]
        [InlineData("enhanced-three.eac3", "enhanced-three.pcm", 48000, 1, 135)]
        [InlineData("d25.ac3", "d25.pcm", 48000, 1, 135)]
        [InlineData("d45.ac3", "d45.pcm", 48000, 1, 135)]
        [InlineData("enhanced-d25.eac3", "enhanced-d25.pcm", 48000, 1, 135)]
        [InlineData("enhanced-d45.eac3", "enhanced-d45.pcm", 48000, 1, 135)]
        public void DecodedPcmMatchesIndependentReference(string encodedName, string referenceName, int rate, int channels, double minimumSnr)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", encodedName));
            byte[] reference = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", referenceName));
            ReadOnlySpan<float> expected = MemoryMarshal.Cast<byte, float>(reference);
            float[] actual = new float[expected.Length];
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (Ac3FrameDecoder decoder = new(allocator))
            {
                int offset = 0, samples = 0;
                while (offset < encoded.Length)
                {
                    Ac3FrameHeader header = Ac3FrameHeader.Read(encoded.AsSpan(offset));
                    Assert.Equal(rate, header.SampleRate); Assert.Equal(channels, header.ChannelCount);
                    ReadOnlySpan<byte> frame = encoded.AsSpan(offset, header.FrameBytes);
                    Ac3FrameDecoder.ValidateFrame(frame, header);
                    decoder.Decode(frame, header, actual.AsSpan(samples), CancellationToken.None);
                    samples += header.SamplesPerFrame * channels; offset += header.FrameBytes;
                }
                Assert.Equal(expected.Length, samples);
            }
            double error = 0, signal = 0, max = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.True(float.IsFinite(actual[i]));
                int expectedIndex = i;
                if (channels == 6)
                {
                    ReadOnlySpan<int> order = [0, 2, 1, 4, 5, 3];
                    expectedIndex = i / channels * channels + order[i % channels];
                }
                double delta = actual[i] - expected[expectedIndex]; error += delta * delta; signal += expected[expectedIndex] * expected[expectedIndex]; max = Math.Max(max, Math.Abs(delta));
            }
            double snr = 10 * Math.Log10(signal / Math.Max(error, double.Epsilon));
            output.WriteLine($"{encodedName}: {actual.Length / channels} samples/channel; SNR {snr:F6} dB; RMS error {Math.Sqrt(error / actual.Length):G9}; maximum error {max:G9}.");
            Assert.True(snr >= minimumSnr, $"SNR {snr:F3} dB; maximum error {max:G9}.");
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
