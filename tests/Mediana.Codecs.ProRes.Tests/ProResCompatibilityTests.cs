// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Codecs.ProRes.Tests
{
    public class ProResCompatibilityTests
    {
        [Fact]
        public async Task ProbeReadsOpeningVideoFactsThroughTheContainerPrefixContract()
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddStreamMetadataReader(new ProResStreamMetadataReader()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "hq.mov")));
            MediaInfo info = await engine.ProbeAsync(source);
            VideoStreamMetadata video = Assert.IsType<VideoStreamMetadata>(info.StreamMetadata[0]);
            Assert.Equal(10, Assert.Single(video.Configurations).LumaBitDepth);
        }

        [Fact]
        public async Task LegacyAlphaRecoveryIsExplicitAndReportsWarning()
        {
            MediaBufferAllocator packets = new(1024 * 1024), output = new(1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "4444.mov")));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, packets);
            using MediaPacket packet = await reader.ReadPacketAtAsync(0, 0);
            using (IMediaCodec<MediaPacket, VideoFrame> strict = new ProResDecoderFactory().Create(reader.Streams[0], output))
            {
                Assert.Throws<InvalidDataException>(() => strict.TrySend(packet));
            }
            Assert.Equal(0, output.AllocatedBytes);
            List<string> warnings = [];
            using (IMediaCodec<MediaPacket, VideoFrame> compatible = new ProResDecoderFactory(new() { RecoverLegacyAlpha = true }, warnings.Add)
                .Create(reader.Streams[0], output))
            {
                compatible.TrySend(packet);
                using VideoFrame frame = compatible.Receive().Value;
                Assert.Equal(4, frame.Planes.Count);
                Assert.Single(warnings);
            }
            Assert.Equal(0, output.AllocatedBytes);
            ProResStreamMetadataReader metadata = new();
            Assert.Null(metadata.Read(reader.Streams[0]));
            byte[] prefix = new byte[metadata.PacketProbeLength];
            Assert.Equal(prefix.Length, await reader.ReadPacketPrefixAtAsync(0, 0, prefix));
            Assert.Equal(packet.Data.Span[..prefix.Length].ToArray(), prefix);
            VideoStreamMetadata facts = Assert.IsType<VideoStreamMetadata>(metadata.ReadPacketPrefix(reader.Streams[0], prefix));
            VideoStreamConfiguration configuration = Assert.Single(facts.Configurations);
            Assert.Equal(82, configuration.Width);
            Assert.Equal(74, configuration.Height);
            Assert.Equal(12, configuration.LumaBitDepth);
            Assert.Equal(VideoChromaSampling.Sampling444, configuration.ChromaSampling);
        }
    }
}
