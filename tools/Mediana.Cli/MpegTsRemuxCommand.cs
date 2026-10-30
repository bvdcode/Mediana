// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Cli
{
    internal static class MpegTsRemuxCommand
    {
        private const long BufferBudgetBytes = 64 * 1024 * 1024;

        public static async Task<int> RunAsync(string inputPath, string outputPath, int? streamIndex = null, CancellationToken cancellationToken = default)
        {
            MediaEngine engine = ContainerEngineFactory.CreateBuilder(Mp4TimestampMode.LinearMovie).Build();
            MediaOpenOptions options = new() { BufferBudgetBytes = BufferBudgetBytes };
            long packets = 0;
            int streams;
            await using (RandomAccessStreamByteSource source = new(File.OpenRead(inputPath)))
            await using (ContainerSession reader = await engine.OpenContainerAsync(source, options, cancellationToken))
            {
                EncodedStreamInfo[] selected = streamIndex is int index
                    ? [reader.Streams.SingleOrDefault(item => item.StreamIndex == index)
                        ?? throw new ArgumentException("The requested stream does not exist.", nameof(streamIndex))]
                    : reader.Streams.ToArray();
                streams = selected.Length;
                MediaBufferAllocator allocator = new(BufferBudgetBytes);
                await CommandOutputFile.WriteAsync(outputPath, async output =>
                {
                    await using MpegTsWriter writer = MpegTsWriter.Create(output, selected, allocator);
                    while (await reader.ReadPacketAsync(cancellationToken) is MediaPacket packet)
                    {
                        using (packet)
                        {
                            if (streamIndex is null || packet.StreamIndex == streamIndex)
                            {
                                await writer.WritePacketAsync(packet, cancellationToken);
                                packets++;
                            }
                        }
                    }
                    await writer.CompleteAsync(cancellationToken);
                }, cancellationToken: cancellationToken);
                await reader.DisposeAsync();
                if (allocator.AllocatedBytes != 0 || reader.AllocatedBufferBytes != 0)
                {
                    throw new InvalidOperationException("Stream-copy buffers were not released.");
                }
            }
            await Console.Out.WriteLineAsync($"MPEG-TS stream copy: {streams} streams; {packets} packets; timestamps + 1 second. Live buffer bytes: 0.");
            return 0;
        }
    }
}
