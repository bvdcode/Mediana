// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Containers.Matroska;
using Mediana.Core.Audio;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Codecs.Dts.Tests
{
    public class DtsDecodeTests
    {
        [Theory]
        [InlineData("stereo", 48000, 2)]
        [InlineData("surround", 48000, 6)]
        [InlineData("mono44100", 44100, 1)]
        public Task CompleteWaveformAt400HzPreservesSamplesAndWindowTimes(string name, int rate, int channels) =>
            Mediana.Tests.DtsWaveformTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"), name, rate, channels);

        [Theory]
        [InlineData("stereo")]
        [InlineData("surround")]
        [InlineData("mono44100")]
        [InlineData("noise")]
        [InlineData("vectors")]
        [InlineData("predictors")]
        public async Task CompleteSequenceMatchesIndependentDecoder(string name)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + ".pcm"));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory())
                .AddAudioDecoder(new DtsDecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".mkv")));
            await using AudioSession session = await engine.OpenAudioAsync(source);
            int position = 0;
            List<(MediaTimestamp Time, byte[] Samples)> decoded = [];
            while (await session.ReadFrameAsync() is AudioFrame frame)
            {
                using (frame)
                {
                    decoded.Add((frame.Timestamp!.Value, frame.GetPlane(0).ToArray()));
                    ReadOnlySpan<float> actual = MemoryMarshal.Cast<byte, float>(frame.GetPlane(0).Span);
                    ReadOnlySpan<float> expected = MemoryMarshal.Cast<byte, float>(reference.AsSpan(position, actual.Length * 4));
                    for (int i = 0; i < actual.Length; i++)
                    {
                        Assert.True(Math.Abs(actual[i] - expected[i]) < 0.00002,
                            $"Sample {position / 4 + i}: {actual[i]} versus {expected[i]}");
                    }
                    position += actual.Length * 4;
                }
            }
            Assert.Equal(reference.Length, position);
            Assert.True(session.CanSeek);
            foreach ((MediaTimestamp time, byte[] samples) in decoded.AsEnumerable().Reverse())
            {
                await session.SeekAsync(time);
                using AudioFrame frame = await session.ReadFrameAsync() ?? throw new InvalidDataException("Missing DTS seek output.");
                Assert.Equal(time, frame.Timestamp);
                Assert.Equal(samples, frame.GetPlane(0).ToArray());
            }
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
        }
    }
}
