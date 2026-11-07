// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Ogg;
using Mediana.Core.Memory;
using Mediana.Formats;
using Mediana.IO;

namespace Mediana.Codecs.Theora.Tests
{
    internal static class TheoraTestInput
    {
        internal static async Task<List<byte[]>> ReadPacketsAsync(string name)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using StreamByteSource source = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", name + ".ogv")));
            List<byte[]> result = [];
            await using (OggPacketReader packets = new(source, allocator))
            {
                while (await packets.ReadPacketAsync() is OggPacket packet)
                { using (packet) { result.Add(packet.Data.ToArray()); } }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            return result;
        }

        internal static byte[] Configuration(List<byte[]> packets) => XiphLacedHeaders.Write(packets[0], packets[1], packets[2]);
    }
}
