// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.ProcessingBenchmarks
{
    internal static class PacketBenchmark
    {
        public static async Task RunAsync(string[] args)
        {
            if (args.Length != 3)
            {
                throw new ArgumentException("Use packets input.mp4 file|memory iterations.");
            }

            int iterations = int.Parse(args[2], CultureInfo.InvariantCulture);
            ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
            await using Stream input = args[1] switch
            {
                "file" => new FileStream(args[0], FileMode.Open, FileAccess.Read, FileShare.Read,
                    65536, FileOptions.Asynchronous | FileOptions.RandomAccess),
                "memory" => new ReadOnlyMemoryStream(await File.ReadAllBytesAsync(args[0])),
                _ => throw new ArgumentException("The source must be file or memory.")
            };
            MediaBufferAllocator allocator = new(1024L * 1024 * 1024, MemoryPool<byte>.Shared);
            using Process process = Process.GetCurrentProcess();
            long allocated = GC.GetTotalAllocatedBytes(true);
            TimeSpan cpu = process.TotalProcessorTime;
            Stopwatch watch = Stopwatch.StartNew();
            long packets = 0;
            long payload = 0;
            long reads = 0;
            long pendingReads = 0;
            long sourceBytes = 0;
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                input.Position = 0;
                await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
                await using MeasuredByteSource measured = new(source);
                await using (Mp4Reader reader = await Mp4Reader.OpenAsync(measured, allocator))
                {
                    while (await reader.ReadPacketAsync() is MediaPacket packet)
                    {
                        using (packet)
                        {
                            packets++;
                            payload += packet.Data.Length;
                        }
                    }
                }
                reads += measured.Reads;
                pendingReads += measured.PendingReads;
                sourceBytes += measured.BytesRead;
            }
            watch.Stop();
            cpu = process.TotalProcessorTime - cpu;
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            process.Refresh();
            if (allocator.AllocatedBytes != 0 || packets == 0)
            {
                throw new InvalidOperationException("Packet reading retained storage or returned no packets.");
            }
            Console.WriteLine(FormattableString.Invariant($"packets: elapsed-ms={watch.Elapsed.TotalMilliseconds:F3}; cpu-ms={cpu.TotalMilliseconds:F3}; peak-rss-bytes={process.PeakWorkingSet64}; allocated-bytes={allocated}; packets={packets}; payload-bytes={payload}; source-bytes={sourceBytes}; source-reads={reads}; pending-reads={pendingReads}"));
        }
    }
}
