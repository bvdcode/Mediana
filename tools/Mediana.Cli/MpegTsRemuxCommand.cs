// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core.Memory;
using Mediana.Execution;
using Mediana.IO;

namespace Mediana.Cli
{
    internal static class MpegTsRemuxCommand
    {
        private const long BufferBudgetBytes = 64 * 1024 * 1024;

        public static async Task<int> RunAsync(string inputPath, CommandOutputFile outputFile, int? streamIndex = null, CancellationToken cancellationToken = default)
        {
            MediaEngine engine = ContainerEngineFactory.CreateBuilder(Mp4TimestampMode.LinearMovie)
                .AddContainerWriter(new MpegTsWriterFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(inputPath));
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            MediaOperationProgress? result = null;
            await outputFile.WriteAsync(async output =>
            {
                result = await engine.RemuxAsync(source, output, "mpegts", allocator,
                    streamIndex is int index ? [index] : null, cancellationToken: cancellationToken);
            }, cancellationToken);
            if (allocator.AllocatedBytes != 0) { throw new InvalidOperationException("Stream-copy buffers were not released."); }
            await Console.Out.WriteLineAsync($"MPEG-TS stream copy: {result!.Packets} packets; timestamps + 1 second. Live buffer bytes: 0.");
            return 0;
        }
    }
}
