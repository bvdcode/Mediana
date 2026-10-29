// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Cli
{
    internal static class Mp4RemuxCommand
    {
        private const long BufferBudgetBytes = 64 * 1024 * 1024;

        public static async Task<int> RunAsync(string inputPath, string outputPath)
        {
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            Mp4ReadOptions options = new() { TimestampMode = Mp4TimestampMode.LinearMovie };
            long packets = 0;
            int streams;
            await using (RandomAccessStreamByteSource source = new(File.OpenRead(inputPath)))
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, options))
            {
                streams = reader.Streams.Count;
                await CommandOutputFile.WriteAsync(outputPath, async output =>
                {
                    await using MpegTsWriter writer = MpegTsWriter.Create(output, reader.Streams, allocator);
                    while (await reader.ReadPacketAsync() is MediaPacket packet)
                    {
                        using (packet)
                        {
                            await writer.WritePacketAsync(packet);
                            packets++;
                        }
                    }
                    await writer.CompleteAsync();
                });
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Stream-copy buffers were not released.");
            }
            await Console.Out.WriteLineAsync($"MPEG-TS stream copy: {streams} streams; {packets} packets; linear movie timestamps + 1 second. Live buffer bytes: 0.");
            return 0;
        }
    }
}
