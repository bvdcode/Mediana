// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Containers.Ogg;
using Mediana.Core.Audio;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Codecs.Vorbis.Tests
{
    public class VorbisDecoderTests
    {
        [Fact]
        public async Task WholeOggPcmPreservesTheSampleGridAndEndGranule()
        {
            byte[] input = await File.ReadAllBytesAsync(DataPath("vorbis-stereo.ogg"));
            byte[] expected = await File.ReadAllBytesAsync(DataPath("ogg-stereo.f32le"));
            await using RandomAccessStreamByteSource source = new(new MemoryStream(input));
            MediaEngine engine = CreateEngine();
            await using AudioSession session = await engine.OpenAudioAsync(source);
            int written = 0;
            while (await session.ReadFrameAsync() is AudioFrame frame)
            {
                using (frame)
                {
                    Assert.Equal(new MediaTimestamp(written / 8, new(1, 44100)), frame.Timestamp);
                    Compare(frame.GetPlane(0).Span, expected.AsSpan(written, frame.GetPlane(0).Length));
                    written += frame.GetPlane(0).Length;
                }
            }
            Assert.Equal(expected.Length, written);
            await session.DisposeAsync(); Assert.Equal(0, session.AllocatedBufferBytes);
        }

        [Fact]
        public async Task RepeatedAndReverseSeeksMatchIndependentContinuousPcm()
        {
            byte[] input = await File.ReadAllBytesAsync(DataPath("vorbis-stereo.ogg"));
            byte[] expected = await File.ReadAllBytesAsync(DataPath("ogg-stereo.f32le"));
            await using RandomAccessStreamByteSource source = new(new MemoryStream(input));
            await using AudioSession session = await CreateEngine().OpenAudioAsync(source);
            Assert.True(session.CanSeek);
            foreach (int sample in new[] { 55000, 24000, 0, 55000, 0 })
            {
                await session.SeekAsync(new(sample, new(1, 44100)));
                using AudioFrame frame = (await session.ReadFrameAsync())!;
                Assert.NotNull(frame.Timestamp);
                MediaTimestamp timestamp = frame.Timestamp.Value.Rescale(new(1, 44100), TimestampRounding.NearestEven);
                Assert.InRange(timestamp.Value, Math.Max(0, sample - 2048), sample);
                Compare(frame.GetPlane(0).Span, expected.AsSpan(checked((int)timestamp.Value * 8), frame.GetPlane(0).Length));
            }
            await session.DisposeAsync(); Assert.Equal(0, session.AllocatedBufferBytes);
        }

        private static MediaEngine CreateEngine() => new MediaEngineBuilder().AddContainer(new OggReaderFactory())
            .AddAudioDecoder(new VorbisDecoderFactory()).Build();

        private static string DataPath(string name) => Path.Combine(AppContext.BaseDirectory, "Data", name);

        private static void Compare(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> reference)
        {
            ReadOnlySpan<float> actual = MemoryMarshal.Cast<byte, float>(bytes), expected = MemoryMarshal.Cast<byte, float>(reference);
            double energy = 0, error = 0, peak = 0, difference = 0;
            for (int index = 0; index < actual.Length; index++)
            {
                Assert.True(float.IsFinite(actual[index]));
                double delta = actual[index] - expected[index];
                energy += (double)expected[index] * expected[index]; error += delta * delta;
                peak = Math.Max(peak, Math.Abs(expected[index])); difference = Math.Max(difference, Math.Abs(delta));
            }
            Assert.True(error <= 1e-10 * Math.Max(energy, 1e-20), $"PCM relative error: {Math.Sqrt(error / Math.Max(energy, 1e-20)):R}.");
            Assert.True(difference <= 3e-6 * Math.Max(1, peak), $"PCM peak error: {difference:R}.");
        }
    }
}
