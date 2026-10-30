// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Execution;
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

        public static async Task<int> RunAsync(string inputPath, CommandOutputFile outputFile, int? streamIndex = null, CancellationToken cancellationToken = default)
        {
            MediaEngine engine = ContainerEngineFactory.CreateBuilder(Mp4TimestampMode.LinearMovie).AddContainerWriter(new MpegTsWriterFactory()).Build();
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
                await outputFile.WriteAsync(async output =>
                {
                    await using IContainerWriter writer = engine.CreateContainerWriter("mpegts", output, selected, allocator);
                    MediaOperationProgress result = await MediaOperations.CopyAsync(reader, writer,
                        selected.Select(stream => stream.StreamIndex).ToArray(), cancellationToken: cancellationToken);
                    packets = result.Packets;
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
