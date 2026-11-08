// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Adts;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class AdtsTransportCopyTestInput
    {
        public static async Task VerifyAsync(string root, string name)
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, name + ".aac"));
            MediaBufferAllocator allocator = new(621);
            using MemoryStream output = new();
            List<(int Stream, byte[] Data, long Pts, long Dts)> expected = [];
            await using (MemoryStream file = new(input, writable: false))
            await using (RandomAccessStreamByteSource source = new(file))
            await using (AdtsIndexedReader reader = await AdtsIndexedReader.OpenAsync(source, allocator))
            {
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
            if (expected.Count != 11 || allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("ADTS transport copy lost packets or retained payload storage.");
            }
        }
    }
}
