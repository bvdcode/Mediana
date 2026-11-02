// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs.Aac;
using Mediana.Containers.Mp4;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class AudioSeekTests
    {
        [Theory]
        [InlineData("Aac", "lc-mono-48")]
        [InlineData("Aac", "lc-stereo-44")]
        [InlineData("AacMultichannel", "layout-6")]
        [InlineData("AacMultichannel", "window-transitions")]
        public async Task EveryPacketRestartAndEofRestartMatchSequentialAndIndependentPcm(string directory, string name)
        {
            foreach (Mp4TimestampMode mode in new[] { Mp4TimestampMode.Media, Mp4TimestampMode.LinearMovie })
            {
                await AacSeekTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", directory), name, mode);
            }
        }

        [Fact]
        public async Task LateSeekReadsOnlyOnePrerollPacketAndTheRequestedOutput()
        {
            byte[] data = await DataAsync();
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource metadataSource = new(data);
            await using Mp4Reader metadata = await Mp4Reader.OpenAsync(metadataSource, allocator);
            int candidate = metadata.Tracks[0].SampleCount - 2;
            Mp4SampleInfo history = metadata.GetSampleInfo(0, candidate - 1);
            Mp4SampleInfo output = metadata.GetSampleInfo(0, candidate);
            await using TrackingRandomAccessSource source = new(data, 13);
            await using AudioSession session = await Engine().OpenAudioAsync(source);
            source.Reads.Clear();
            await session.SeekAsync(output.PresentationTime);
            Assert.Equal(history.Size, source.Reads.Sum(range => range.Length));
            using AudioFrame frame = (await session.ReadFrameAsync())!;
            Assert.Equal(output.PresentationTime, frame.Timestamp);
            Assert.Equal(history.Size + output.Size, source.Reads.Sum(range => range.Length));
            Assert.All(source.Reads, range => Assert.Contains(new[] { history, output },
                sample => range.Offset >= sample.Offset && range.Offset + range.Length <= sample.Offset + sample.Size));
        }

        [Fact]
        public async Task NoiseSubstitutionRestartsPreserveFinitePcmAndIndependentChannelBlockEnergy()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Aac");
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(root, "lc-pns-48.m4a"));
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, "lc-pns-48.f32le"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(bytes);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            await using AudioSession session = await Engine().OpenAudioAsync(source);
            for (int index = reader.Tracks[0].SampleCount - 1; index >= 0; index--)
            {
                await session.SeekAsync(reader.GetSampleInfo(0, index).PresentationTime);
                using AudioFrame frame = (await session.ReadFrameAsync())!;
                foreach (float sample in MemoryMarshal.Cast<byte, float>(frame.GetPlane(0).Span))
                {
                    Assert.True(float.IsFinite(sample));
                }
                int frameBytes = frame.GetPlane(0).Length;
                AacDecodeTestInput.GetVerifiedNoiseEnergy(frame.GetPlane(0).Span,
                    reference.AsSpan(index * frameBytes, frameBytes), frame.Format.Channels.Count);
            }
            await session.DisposeAsync();
            await reader.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task PrerollAndRepeatedSeeksKeepPacketAndFrameBudgetsCumulative(bool frames)
        {
            await using TrackingRandomAccessSource source = new(await DataAsync());
            MediaOpenOptions options = frames ? new() { MaxFrameReads = 1 } : new() { MaxPacketReads = 2 };
            await using AudioSession session = await Engine().OpenAudioAsync(source, options);
            await session.SeekAsync(new(10 * 1024, new(1, 48000)));
            using AudioFrame first = (await session.ReadFrameAsync())!;
            if (frames)
            {
                await session.SeekAsync(new(0, new(1, 1)));
                await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReadFrameAsync().AsTask());
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => session.SeekAsync(new(0, new(1, 1))).AsTask());
            }
            first.Dispose();
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
            Assert.False(source.IsDisposed);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SeekGuardsConcurrentOperationsAndCancellationReleasesStorage(bool cancel)
        {
            await using BlockingRandomAccessSource source = new(await DataAsync());
            await using AudioSession session = await Engine().OpenAudioAsync(source);
            await Assert.ThrowsAsync<ArgumentException>(() => session.SeekAsync(default).AsTask());
            source.BlockReads = true;
            using CancellationTokenSource cancellation = new();
            Task seek = session.SeekAsync(new(1, new(1, 4)), cancellation.Token).AsTask();
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReadFrameAsync().AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.SeekAsync(new(0, new(1, 1))).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.DisposeAsync().AsTask());
            if (cancel)
            {
                await cancellation.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => seek);
                await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReadFrameAsync().AsTask());
            }
            else
            {
                source.Resume.TrySetResult();
                await seek;
                using AudioFrame frame = (await session.ReadFrameAsync())!;
            }
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
        }

        private static Task<byte[]> DataAsync() => File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac", "lc-mono-48.m4a"));

        private static MediaEngine Engine() => new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
            .AddAudioDecoder(new AacDecoderFactory()).Build();
    }
}
