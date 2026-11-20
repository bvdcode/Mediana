// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Pcm;
using Mediana.Containers.Avi;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats.Audio;
using Mediana.Processing;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class AviOnDemandHlsTests
    {
        [Fact]
        public async Task AudiovisualColdSegmentsAreIndependentOfRequestOrderAndReadOnlyLocalPayload()
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Avi", "mjpeg-pcm.avi"));
            MediaBufferAllocator allocator = new(8 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(input);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new AviReaderFactory()).AddVideoDecoder(new MjpegDecoderFactory()).Build();
            VideoFormat format;
            await using (VideoSession probe = await engine.OpenVideoAsync(source))
            {
                using VideoFrame frame = await probe.ReadFrameAsync() ?? throw new InvalidDataException("Missing AVI picture.");
                format = frame.Format;
            }
            await using (AviReader reader = await AviReader.OpenAsync(source, allocator))
            {
                AudioFormat audio = PcmConfiguration.ReadWaveFormat(reader.Streams[1].Configuration.Span, out _)!.Format;
                source.Reads.Clear();
                long retained = allocator.AllocatedBytes;
                await using AvcHlsTranscodeSession session = AvcHlsTranscodeSession.Create(reader, 0, new MjpegDecoderFactory(), format,
                    new MediaDuration(500, new(1, 1000)), allocator,
                    preparation: new VideoPreparationOptions { RgbConversion = new() },
                    audioEncoding: new HlsAacEncoding(1, new PcmDecoderFactory(), audio, new(new(0, new(1, 48000)), new(72000, new(1, 48000)))));
                Assert.Empty(source.Reads);
                Assert.Equal(2, session.Streams.Count);
                Assert.Equal(3, session.Windows.Count);
                using MemoryStream initialization = new();
                await session.WriteInitializationAsync(initialization);
                Assert.Empty(source.Reads);
                byte[]? repeated = null;
                foreach (int index in new[] { 2, 0, 1, 2 })
                {
                    source.Reads.Clear();
                    using MemoryStream fragment = new();
                    HlsMediaSegment result = await session.WriteSegmentAsync(index, fragment);
                    Assert.Equal(new MediaTimestamp(index * 500, new(1, 1000)), result.Start);
                    Assert.True(fragment.Length > 0);
                    Assert.True(source.Reads.Sum(read => read.Length) < input.Length * 3 / 4);
                    Assert.Equal(retained, allocator.AllocatedBytes);
                    if (index == 2)
                    {
                        if (repeated is not null) { Assert.Equal(repeated, fragment.ToArray()); }
                        repeated = fragment.ToArray();
                    }
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
