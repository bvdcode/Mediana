// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Codecs.Aac;
using Mediana.Containers.Adts;
using Mediana.Core.Audio;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    public class AdtsDecodeTests
    {
        [Theory]
        [InlineData("lc-mono-48", 48000, 1)]
        [InlineData("lc-stereo-44", 44100, 2)]
        public async Task IndependentAdtsFilesDecodeThroughThePublicEngine(string name, int rate, int channels)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, name + ".aac"));
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + ".f32le"));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new AdtsReaderFactory())
                .AddAudioDecoder(new AacDecoderFactory()).Build();
            await using ShortReadSource source = new(input);
            await using AudioSession session = await engine.OpenAudioAsync(source);
            long total = 0;
            double maximumError = 0;
            while (await session.ReadFrameAsync() is AudioFrame frame)
            {
                using (frame)
                {
                    Assert.Equal(rate, frame.Format.SampleRate);
                    Assert.Equal(channels, frame.Format.Channels.Count);
                    Assert.Equal(new MediaTimestamp(total, new TimeBase(1, rate)), frame.Timestamp);
                    Assert.Equal(1024, frame.SamplesPerChannel);
                    ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(frame.GetPlane(0).Span);
                    long offset = checked(total * channels * sizeof(float));
                    Assert.True(offset + samples.Length * sizeof(float) <= reference.Length);
                    for (int index = 0; index < samples.Length; index++)
                    {
                        Assert.True(float.IsFinite(samples[index]));
                        float expected = BinaryPrimitives.ReadSingleLittleEndian(reference.AsSpan(checked((int)offset + index * sizeof(float))));
                        maximumError = Math.Max(maximumError, Math.Abs(samples[index] - expected));
                    }
                    total += frame.SamplesPerChannel;
                }
            }
            Assert.Equal(reference.Length, total * channels * sizeof(float));
            Assert.True(maximumError <= 2e-6, $"Maximum independent PCM error: {maximumError:G9}.");
            Assert.Null(await session.ReadFrameAsync());
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
            Assert.False(source.IsDisposed);
        }
    }
}
