// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Ogg;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;
using Mediana.Tests;

namespace Mediana.Codecs.Theora.Tests
{
    public class TheoraContainerTests
    {
        [Theory]
        [InlineData("fractional")]
        [InlineData("av")]
        [InlineData("main420")]
        [InlineData("chroma422")]
        [InlineData("chroma444")]
        [InlineData("motion")]
        [InlineData("noise")]
        [InlineData("odd420")]
        [InlineData("duplicate")]
        public Task SharedVideoApiDecodesPreviewsSeeksAndEncodesTheCompleteStream(string name) =>
            TheoraContainerTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"), name);

        [Fact]
        public async Task IndexExposesExactClocksMetadataKeyframesAndPreroll()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "main420.ogv")));
            await using (OggReader reader = await OggReader.OpenAsync(source, allocator))
            {
                Assert.Equal("theora", reader.Streams[0].CodecId); Assert.Equal(MediaKind.Video, reader.Streams[0].Kind);
                Assert.Equal(24, reader.GetPacketCount(0)); Assert.Equal(24, reader.Info.DeclaredDuration!.Value.Value);
                Assert.Equal(12, reader.Info.StreamInfo![0].AverageFrameRate!.Value.Numerator);
                Assert.Equal(24, reader.Info.StreamInfo[0].PacketIndex!.PacketCount);
                for (int i = 0; i < 24; i++)
                {
                    using MediaPacket packet = await reader.ReadPacketAtAsync(0, i);
                    Assert.Equal(i, packet.PresentationTime!.Value.Value); Assert.Equal(1, packet.Duration!.Value.Value);
                    Assert.Equal(i % 12 == 0, packet.IsKeyFrame);
                    Assert.Equal(i / 12 * 12, reader.FindSeekPoint(0, packet.PresentationTime.Value, 24)!.Value.PacketIndex);
                    Assert.Equal(i / 12 * 12, reader.GetPrerollStart(0, i, 0).PacketIndex);
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
