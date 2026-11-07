// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using Mediana.Codecs.Aac;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.ProcessingBenchmarks
{
    internal static class PipelineBenchmark
    {
        public static async Task RunAsync(string[] args)
        {
            bool pooled = args.Length > 0 && args[^1] == "--pool";
            if (pooled) { args = args[..^1]; }
            if (args.Length is not (4 or 6)) { throw new ArgumentException("Use input.mp4 output.ts width height [start-ms end-ms]."); }
            int width = int.Parse(args[2], CultureInfo.InvariantCulture);
            int height = int.Parse(args[3], CultureInfo.InvariantCulture);
            MediaTimeRange? selection = args.Length == 6 ? new(new(long.Parse(args[4], CultureInfo.InvariantCulture), new(1, 1000)),
                new(long.Parse(args[5], CultureInfo.InvariantCulture), new(1, 1000))) : null;
            MediaEngine engine = new MediaEngineBuilder().AddVideoDecoder(new H264DecoderFactory())
                .AddAudioDecoder(new AacDecoderFactory()).Build();
            MediaBufferAllocator allocator = pooled ? new(1024L * 1024 * 1024, MemoryPool<byte>.Shared) : new(1024L * 1024 * 1024);
            using Process process = Process.GetCurrentProcess();
            long allocated = GC.GetTotalAllocatedBytes(true);
            TimeSpan cpu = process.TotalProcessorTime;
            Stopwatch watch = Stopwatch.StartNew();
            MediaProcessingResult result;
            double? firstPacket;
            long readBytes;
            long reads;
            long outputBytes;
            await using (FileStream input = File.OpenRead(args[0]))
            await using (RandomAccessStreamByteSource source = new(input, leaveOpen: true))
            await using (MeasuredByteSource measured = new(source))
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(measured, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                List<MediaTrackRequest> requests = [];
                foreach (EncodedStreamInfo stream in reader.Streams)
                {
                    switch (stream.Kind)
                    {
                        case MediaKind.Video:
                            requests.Add(new VideoEncodingRequest(stream.StreamIndex, stream.StreamIndex, new H264EncoderFactory(),
                                new VideoPreparationOptions { MaximumWidth = width, MaximumHeight = height }));
                            break;
                        case MediaKind.Audio:
                            requests.Add(new AudioEncodingRequest(stream.StreamIndex, stream.StreamIndex, new AacEncoderFactory()));
                            break;
                        case MediaKind.Data:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(args));
                    }
                }
                await using MediaProcessingSession session = await engine.OpenProcessingAsync(reader, requests, allocator, new() { Selection = selection });
                await using FileStream output = new(args[1], FileMode.CreateNew, FileAccess.Write);
                await using MpegTsWriter writer = MpegTsWriter.Create(output, session.Streams, allocator);
                await using MeasuredPacketWriter measuredWriter = new(writer, watch);
                result = await session.WriteAsync(measuredWriter);
                firstPacket = measuredWriter.FirstPacketMilliseconds;
                readBytes = measured.BytesRead;
                reads = measured.Reads;
                outputBytes = output.Length;
            }
            watch.Stop();
            cpu = process.TotalProcessorTime - cpu;
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            process.Refresh();
            if (allocator.AllocatedBytes != 0) { throw new InvalidOperationException("The completed operation retained media storage."); }
            Console.WriteLine(FormattableString.Invariant($"pipeline: elapsed-ms={watch.Elapsed.TotalMilliseconds:F3}; cpu-ms={cpu.TotalMilliseconds:F3}; first-packet-ms={firstPacket:F3}; peak-rss-bytes={process.PeakWorkingSet64}; allocated-bytes={allocated}; source-bytes={readBytes}; source-reads={reads}; output-bytes={outputBytes}; packets={result.Packets}"));
            foreach (MediaTrackResult track in result.Tracks)
            {
                Console.WriteLine(FormattableString.Invariant($"track: index={track.Stream.StreamIndex}; kind={track.Stream.Kind}; frames={track.Frames}; samples={track.SamplesPerChannel}; packets={track.Packets}; payload-bytes={track.PayloadBytes}; priming={track.PrimingSamples}; padding={track.PaddingSamples}"));
            }
        }
    }
}
