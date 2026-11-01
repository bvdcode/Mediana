// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Containers.Mp4;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class IndexedAudioReadTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(13)]
        public async Task CompleteAudioDecodeReadsOnlyItsSelectedPackets(int maximumRead)
        {
            byte[] bytes = await Mp4TestData.ReadAsync("avc-aac.mp4");
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource metadataSource = new(bytes);
            await using Mp4Reader metadata = await Mp4Reader.OpenAsync(metadataSource, allocator);
            Mp4SampleInfo[] samples = Enumerable.Range(0, metadata.Tracks[1].SampleCount)
                .Select(index => metadata.GetSampleInfo(1, index)).ToArray();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddAudioDecoder(new AacDecoderFactory()).Build();
            await using TrackingRandomAccessSource source = new(bytes, maximumRead);
            await using AudioSession session = await engine.OpenAudioAsync(source, 1,
                new MediaOpenOptions { MaxPacketReads = samples.LongLength + 1 });
            source.Reads.Clear();
            int frames = 0;
            while (true)
            {
                using AudioFrame? frame = await session.ReadFrameAsync();
                if (frame is null)
                {
                    break;
                }
                Assert.True(frame.SamplesPerChannel > 0);
                frames++;
            }
            Assert.True(frames > 0);
            Assert.Equal(samples.Sum(sample => (long)sample.Size), source.Reads.Sum(range => (long)range.Length));
            Assert.All(source.Reads, range => Assert.Contains(samples, sample =>
                range.Offset >= sample.Offset && range.Offset + range.Length <= sample.Offset + sample.Size));
            Assert.Null(await session.ReadFrameAsync());
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
            Assert.False(source.IsDisposed);
        }
    }
}
