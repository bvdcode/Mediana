// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Adts;
using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    public class AdtsTransportCopyTests
    {
        [Theory]
        [InlineData("multi-protected")]
        [InlineData("multi-unprotected")]
        public async Task ExistingAdtsFramesReachTransportStreamWithoutWrappingOrAnotherPayloadAllocation(string name)
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Adts", name + ".aac"));
            MediaBufferAllocator allocator = new(621);
            using MemoryStream output = new();
            List<(int Stream, byte[] Data, long Pts, long Dts)> expected = [];
            await using (MemoryStream file = new(input, writable: false))
            await using (RandomAccessStreamByteSource source = new(file))
            await using (AdtsIndexedReader reader = await AdtsIndexedReader.OpenAsync(source, allocator))
            {
                Assert.Throws<NotSupportedException>(() => new Mp4AudioTrackDefinition(reader.Streams[0], 1));
                await using (MpegTsWriter writer = MpegTsWriter.Create(output, reader.Streams, allocator))
                {
                    while (await reader.ReadPacketAsync() is MediaPacket packet)
                    {
                        using (packet)
                        {
                            expected.Add((0, packet.Data.ToArray(), MpegTsTestInput.Ticks(packet.PresentationTime), MpegTsTestInput.Ticks(packet.DecodeTime)));
                            await writer.WritePacketAsync(packet);
                        }
                    }
                    await writer.CompleteAsync();
                }
                MpegTsTestInput.Verify(output.ToArray(), reader.Streams, expected);
            }
            Assert.Equal(11, expected.Count);
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
