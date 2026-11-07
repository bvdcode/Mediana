// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using Mediana.Codecs.Aac;
using Mediana.Codecs.H264;
using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats.Audio;
using Mediana.IO;
using Mediana.Processing;
using Mediana.Streaming.Hls;

namespace Mediana.ProcessingBenchmarks
{
    internal static class HlsBenchmark
    {
        public static async Task RunAsync(string[] args)
        {
            if (args.Length != 5) { throw new ArgumentException("Use input.mp4 output-directory width height segment-number|all."); }
            int width = int.Parse(args[2], CultureInfo.InvariantCulture);
            int height = int.Parse(args[3], CultureInfo.InvariantCulture);
            Directory.CreateDirectory(args[1]);
            MediaBufferAllocator allocator = new(1024L * 1024 * 1024, MemoryPool<byte>.Shared);
            using Process process = Process.GetCurrentProcess();
            long allocated = GC.GetTotalAllocatedBytes(true);
            TimeSpan cpu = process.TotalProcessorTime;
            Stopwatch watch = Stopwatch.StartNew();
            await using (FileStream input = File.OpenRead(args[0]))
            await using (RandomAccessStreamByteSource source = new(input, leaveOpen: true))
            await using (MeasuredByteSource measured = new(source))
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(measured, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                EncodedStreamInfo video = reader.Streams.First(stream => stream.Kind == MediaKind.Video);
                VideoStreamConfiguration description = ((VideoStreamMetadata)new H264StreamMetadataReader().Read(video)!).Configurations.Single();
                if (description.ChromaSampling != VideoChromaSampling.Sampling420 || description.LumaBitDepth != 8 || description.ChromaBitDepth != 8)
                { throw new NotSupportedException("This comparison requires eight-bit 4:2:0 AVC input."); }
                VideoFormat format = new(description.Width, description.Height, VideoPixelFormat.Yuv420P8, description.Color, description.SampleAspectRatio);
                HlsAacEncoding? audio = CreateAudio(reader);
                await using AvcHlsTranscodeSession session = AvcHlsTranscodeSession.Create(reader, video.StreamIndex, new H264DecoderFactory(), format,
                    new MediaDuration(1, new(1, 1)), allocator, preparation: new VideoPreparationOptions { MaximumWidth = width, MaximumHeight = height }, audioEncoding: audio);
                await using (FileStream init = new(Path.Combine(args[1], "init.mp4"), FileMode.CreateNew, FileAccess.Write))
                { await session.WriteInitializationAsync(init); }
                Console.WriteLine(FormattableString.Invariant($"opening: elapsed-ms={watch.Elapsed.TotalMilliseconds:F3}; source-bytes={measured.BytesRead}; source-reads={measured.Reads}; live-bytes={allocator.AllocatedBytes}; segments={session.Windows.Count}"));
                IEnumerable<int> numbers = args[4] == "all" ? Enumerable.Range(0, session.Windows.Count).Reverse() : [int.Parse(args[4], CultureInfo.InvariantCulture)];
                foreach (int number in numbers)
                {
                    long beforeRead = measured.BytesRead;
                    TimeSpan before = watch.Elapsed;
                    await using FileStream output = new(Path.Combine(args[1], $"{number}.m4s"), FileMode.CreateNew, FileAccess.Write);
                    HlsMediaSegment segment = await session.WriteSegmentAsync(number, output);
                    Console.WriteLine(FormattableString.Invariant($"segment: number={number}; elapsed-ms={(watch.Elapsed - before).TotalMilliseconds:F3}; source-bytes={measured.BytesRead - beforeRead}; output-bytes={segment.ByteLength}; live-bytes={allocator.AllocatedBytes}"));
                }
                await File.WriteAllTextAsync(Path.Combine(args[1], "index.m3u8"), session.CreateVodPlaylist("init.mp4", number => $"{number}.m4s"));
            }
            watch.Stop();
            cpu = process.TotalProcessorTime - cpu;
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            process.Refresh();
            if (allocator.AllocatedBytes != 0) { throw new InvalidOperationException("HLS processing retained media storage."); }
            Console.WriteLine(FormattableString.Invariant($"hls: elapsed-ms={watch.Elapsed.TotalMilliseconds:F3}; cpu-ms={cpu.TotalMilliseconds:F3}; peak-rss-bytes={process.PeakWorkingSet64}; allocated-bytes={allocated}; live-bytes={allocator.AllocatedBytes}"));
        }

        private static HlsAacEncoding? CreateAudio(Mp4Reader reader)
        {
            EncodedStreamInfo? stream = reader.Streams.FirstOrDefault(item => item.Kind == MediaKind.Audio);
            if (stream is null) { return null; }
            AacConfiguration configuration = AacConfiguration.ReadAudioSpecificConfig(stream.Configuration.Span, out string? reason)
                ?? throw new NotSupportedException(reason);
            string[] channels = configuration.ChannelConfiguration switch
            {
                1 => ["front-center"],
                2 => ["front-left", "front-right"],
                _ => throw new NotSupportedException("This comparison requires mono or stereo AAC input.")
            };
            MediaTimeRange presentation = ((IContainerPresentationReader)reader).GetPresentationRange(stream.StreamIndex)
                ?? throw new NotSupportedException("This comparison requires explicit source audio presentation bounds.");
            return new(stream.StreamIndex, new AacDecoderFactory(), new(configuration.SampleRate, AudioSampleFormat.Float32,
                AudioStorageLayout.Interleaved, channels), presentation, new() { BitRate = configuration.ChannelCount * 64000 });
        }
    }
}
