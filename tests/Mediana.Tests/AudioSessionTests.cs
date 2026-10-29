// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Pcm;
using Mediana.Containers.Wave;
using Mediana.Core.Audio;
using Mediana.IO;

namespace Mediana.Tests
{
    public class AudioSessionTests
    {
        [Fact]
        public async Task SequentialProbeReplayAndRandomAccessOpeningPreserveSourceOwnership()
        {
            byte[] data = WaveTestInput.File(("fmt ", WaveTestInput.Format(8, 1, 8000)), ("data", [0, 64, 128, 255]));
            MediaEngine engine = Engine();
            await using ShortReadSource sequential = new(data);
            await using (AudioSession session = await engine.OpenAudioAsync(sequential, 0))
            using (AudioFrame frame = (await session.ReadFrameAsync())!)
            {
                Assert.Equal(new byte[] { 0, 64, 128, 255 }, frame.GetPlane(0).ToArray());
                Assert.Null(await session.ReadFrameAsync());
            }
            Assert.False(sequential.IsDisposed);
            using MemoryStream memory = new(data);
            await using RandomAccessStreamByteSource random = new(memory, leaveOpen: true);
            await random.ReadAsync(new byte[3]);
            await using (AudioSession session = await engine.OpenAudioAsync(random, 0))
            using (AudioFrame frame = (await session.ReadFrameAsync())!)
            {
                Assert.Equal(new byte[] { 0, 64, 128, 255 }, frame.GetPlane(0).ToArray());
                Assert.Equal(3, memory.Position);
            }
            Assert.True(memory.CanRead);
            Assert.Equal(3, memory.Position);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CancellationAndReadLimitsFaultTheSessionUntilDisposal(bool cancel)
        {
            byte[] data = WaveTestInput.File(("fmt ", WaveTestInput.Format(8, 1)), ("data", new byte[8]));
            await using ShortReadSource source = new(data);
            await using AudioSession session = await Engine().OpenAudioAsync(source, 0, new MediaOpenOptions { MaxFrameReads = 1 });
            if (cancel)
            {
                await Assert.ThrowsAsync<OperationCanceledException>(async () => await session.ReadFrameAsync(new CancellationToken(true)));
            }
            else
            {
                using AudioFrame frame = (await session.ReadFrameAsync())!;
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadFrameAsync());
            }
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadFrameAsync());
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task KindSelectionUnsupportedDecodersAndRegistrySnapshotsAreExplicit()
        {
            MediaEngineBuilder builder = new MediaEngineBuilder().AddContainer(new WaveReaderFactory());
            MediaEngine noDecoder = builder.Build();
            builder.AddAudioDecoder(new PcmDecoderFactory());
            Assert.Empty(noDecoder.AudioDecoderImplementations);
            Assert.Equal(new[] { "mediana.pcm" }, builder.Build().AudioDecoderImplementations);
            Assert.Throws<ArgumentException>(() => builder.AddAudioDecoder(new PcmDecoderFactory()));
            byte[] data = WaveTestInput.File(("fmt ", WaveTestInput.Format()), ("data", new byte[4]));
            using MemoryStream memory = new(data);
            await using RandomAccessStreamByteSource source = new(memory, leaveOpen: true);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await noDecoder.OpenAudioAsync(source, 0));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await builder.Build().OpenVideoAsync(source, 0));
            await Assert.ThrowsAsync<ArgumentException>(async () => await builder.Build().OpenAudioAsync(source, 1));
            Assert.True(memory.CanRead);
            Assert.Equal(0, memory.Position);
        }

        private static MediaEngine Engine() => new MediaEngineBuilder().AddContainer(new WaveReaderFactory()).AddAudioDecoder(new PcmDecoderFactory()).Build();
    }
}
