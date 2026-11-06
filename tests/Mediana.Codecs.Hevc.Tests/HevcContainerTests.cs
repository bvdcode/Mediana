// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Containers;
using Mediana.Containers.Matroska;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Video;
using Mediana.IO;

namespace Mediana.Codecs.Hevc.Tests
{
    public class HevcContainerTests
    {
        [Theory]
        [InlineData("main8.mp4", 8)]
        [InlineData("main10.mp4", 10)]
        [InlineData("main8.mkv", 8)]
        [InlineData("main10.mkv", 10)]
        public async Task MainAndMain10KeepIndependentPacketBytesPresentationOrderAndProfiles(string name, int depth)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, name + ".json")));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using FileStream file = File.OpenRead(Path.Combine(root, name));
            await using RandomAccessStreamByteSource source = new(file, leaveOpen: true);
            bool mp4 = Path.GetExtension(name) == ".mp4";
            await using (IIndexedContainerReader reader = mp4
                ? await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie })
                : await MatroskaReader.OpenAsync(source, allocator))
            {
                EncodedStreamInfo stream = Assert.Single(reader.Streams);
                Assert.Equal(HevcDecoderConfiguration.CodecId, stream.CodecId);
                Assert.Equal(StreamFormats.Hevc, stream.Format);
                HevcDecoderConfiguration config = HevcDecoderConfiguration.Read(stream.Configuration);
                Assert.Equal(depth, config.LumaBitDepth);
                Assert.Equal(depth, config.ChromaBitDepth);
                Assert.Equal(depth == 10 ? 2 : 1, config.Profile);
                Assert.Equal(1, config.ChromaFormat);
                Assert.Equal(4, config.LengthSize);
                Assert.Contains(config.Arrays, array => array.Type == 32);
                Assert.Contains(config.Arrays, array => array.Type == 33);
                Assert.Contains(config.Arrays, array => array.Type == 34);
                JsonElement expectedStream = reference.RootElement.GetProperty("streams")[0];
                string[] clock = expectedStream.GetProperty("time_base").GetString()!.Split('/');
                TimeBase timeBase = new(int.Parse(clock[0], CultureInfo.InvariantCulture), int.Parse(clock[1], CultureInfo.InvariantCulture));
                JsonElement packets = reference.RootElement.GetProperty("packets");
                Assert.Equal(packets.GetArrayLength(), reader.GetPacketCount(0));
                for (int index = packets.GetArrayLength() - 1; index >= 0; index--)
                {
                    JsonElement expected = packets[index];
                    using MediaPacket packet = await reader.ReadPacketAtAsync(0, index);
                    Assert.Equal(new MediaTimestamp(expected.GetProperty("pts").GetInt64(), timeBase), packet.PresentationTime);
                    if (mp4)
                    {
                        Assert.Equal(new MediaTimestamp(expected.GetProperty("dts").GetInt64(), timeBase), packet.DecodeTime);
                    }
                    else
                    {
                        Assert.Null(packet.DecodeTime);
                    }
                    Assert.Equal("SHA256:" + Convert.ToHexStringLower(SHA256.HashData(packet.Data.Span)), expected.GetProperty("data_hash").GetString());
                    Assert.Equal(expected.GetProperty("flags").GetString()!.Contains('K'), packet.IsKeyFrame);
                }
                ContainerSeekPoint seek = reader.FindSeekPoint(0, new MediaTimestamp(750, new(1, 1000)), reader.GetPacketCount(0))!.Value;
                Assert.Equal(0, seek.PacketIndex);
                Assert.Equal(0, file.Position);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.True(file.CanRead);
        }
    }
}
