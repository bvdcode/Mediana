// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Buffers;
using Mediana.Core.Memory;
using System.Diagnostics;
using System.Security.Cryptography;
using Mediana.Codecs.H264;
using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Vp8;
using Mediana.Containers.Avi;
using Mediana.Containers.Matroska;
using Mediana.Containers.Mp4;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.OperationComparison
{
    internal static class VideoComparison
    {
        internal static async Task RunAsync(string input, string destination, bool pooled = false)
        {
            using Process process = Process.GetCurrentProcess();
            long allocated = GC.GetTotalAllocatedBytes(true);
            TimeSpan cpu = process.TotalProcessorTime;
            Stopwatch watch = Stopwatch.StartNew();
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddContainer(new AviReaderFactory()).AddContainer(new MatroskaReaderFactory())
                .AddVideoDecoder(new Vp8DecoderFactory()).AddVideoDecoder(new MjpegDecoderFactory()).AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            MediaBufferAllocator allocator = pooled ? new(64 * 1024 * 1024, MemoryPool<byte>.Shared) : new(64 * 1024 * 1024);
            await using VideoSession session = await engine.OpenVideoAsync(source, allocator, new(), 0);
            await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            await using StreamWriter writer = new(output);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int pictures = 0;
            while (await session.ReadFrameAsync() is VideoFrame frame)
            {
                using (frame)
                {
                    MediaTimestamp timestamp = frame.Timestamp ?? throw new InvalidDataException("Missing presentation time.");
                    MediaDuration duration = frame.Duration ?? throw new InvalidDataException("Missing frame duration.");
                    if (timestamp.TimeBase != duration.TimeBase)
                    {
                        throw new InvalidDataException("Frame duration uses a different clock.");
                    }
                    if (pictures == 0)
                    {
                        await writer.WriteLineAsync($"#tb 0: {timestamp.TimeBase.Numerator}/{timestamp.TimeBase.Denominator}");
                    }
                    int bytes = 0;
                    for (int plane = 0; plane < frame.Planes.Count; plane++)
                    {
                        for (int row = 0; row < frame.Planes[plane].Height; row++)
                        {
                            hash.AppendData(frame.GetRow(plane, row));
                            bytes = checked(bytes + frame.Planes[plane].RowBytes);
                        }
                    }
                    string digest = Convert.ToHexStringLower(hash.GetHashAndReset());
                    await writer.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                        $"0, {timestamp.Value}, {timestamp.Value}, {duration.Value}, {bytes}, {digest}"));
                    pictures++;
                }
            }
            await session.DisposeAsync();
            if (pictures == 0 || session.AllocatedBufferBytes != 0 || allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Empty decode or retained frame storage.");
            }
            await writer.FlushAsync();
            watch.Stop();
            cpu = process.TotalProcessorTime - cpu;
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            process.Refresh();
            await Console.Out.WriteLineAsync(FormattableString.Invariant($"pictures={pictures}; eof=true; retained_bytes=0; elapsed-ms={watch.Elapsed.TotalMilliseconds:F3}; cpu-ms={cpu.TotalMilliseconds:F3}; allocated-bytes={allocated}; peak-rss-bytes={process.PeakWorkingSet64}"));
        }
    }
}
