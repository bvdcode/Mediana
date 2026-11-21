// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Alac;
using Mediana.Containers.Mp4;
using Mediana.Core.Audio;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Codecs.Alac.Tests
{
    public class AlacTests
    {
        [Theory]
        [InlineData("stereo24")]
        [InlineData("mono16")]
        [InlineData("stereo16")]
        [InlineData("silence24")]
        [InlineData("noise24")]
        [InlineData("surround24")]
        public async Task CompleteSequenceAndReverseSeeksMatchIndependentPcm(string name)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + ".pcm"));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddAudioDecoder(new AlacDecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".m4a")));
            await using AudioSession session = await engine.OpenAudioAsync(source);
            Assert.True(session.CanSeek);
            List<(MediaTimestamp Time, int Offset, int Count)> frames = [];
            int offset = 0;
            AudioFrame? held = null;
            while (await session.ReadFrameAsync() is AudioFrame frame)
            {
                using (frame)
                {
                    int length = frame.GetPlane(0).Length;
                    Assert.Equal(expected.AsSpan(offset, length).ToArray(), frame.GetPlane(0).ToArray());
                    Assert.Equal(new MediaTimestamp(offset / (frame.Format.Channels.Count * 4), new(1, frame.Format.SampleRate)), frame.Timestamp);
                    frames.Add((frame.Timestamp!.Value, offset, length));
                    offset += length;
                    held ??= frame.Retain();
                }
            }
            Assert.Equal(expected.Length, offset);
            foreach ((MediaTimestamp time, int start, int length) in frames.AsEnumerable().Reverse())
            {
                await session.SeekAsync(time);
                using AudioFrame frame = await session.ReadFrameAsync() ?? throw new InvalidDataException("Missing ALAC seek frame.");
                Assert.Equal(time, frame.Timestamp);
                Assert.Equal(expected.AsSpan(start, length).ToArray(), frame.GetPlane(0).ToArray());
            }
            await session.DisposeAsync();
            Assert.NotNull(held);
            Assert.Equal(expected.AsSpan(0, held.GetPlane(0).Length).ToArray(), held.GetPlane(0).ToArray());
            held.Dispose();
            Assert.Equal(0, session.AllocatedBufferBytes);
        }
    }
}
