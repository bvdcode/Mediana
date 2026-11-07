// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Ogg;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.OperationComparison
{
    internal static class PacketComparison
    {
        internal static async Task RunAsync(string input, string destination)
        {
            MediaBufferAllocator allocator = new(64 * 1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            await using OggReader reader = await OggReader.OpenAsync(source, allocator);
            await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, FileOptions.Asynchronous);
            long packets = 0;
            while (await reader.ReadPacketAsync() is MediaPacket packet)
            {
                using (packet)
                {
                    await output.WriteAsync(packet.Data);
                    packets++;
                }
            }
            await reader.DisposeAsync();
            if (packets == 0 || allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Empty packet sequence or retained storage.");
            }
            await Console.Out.WriteLineAsync($"packets={packets}; eof=true; retained_bytes=0");
        }
    }
}
