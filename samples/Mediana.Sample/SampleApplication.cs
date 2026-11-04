// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Execution;
using Mediana.Processing;
using System.Globalization;

namespace Mediana.Sample
{
    internal static class SampleApplication
    {
        public static async Task<int> RunAsync(string[] args)
        {
            if (args.Length == 6 && args[0] == "--prepare-hls")
            {
                return await PreparedHlsSample.RunAsync(args[1], args[2], new(
                    long.Parse(args[3], CultureInfo.InvariantCulture), long.Parse(args[4], CultureInfo.InvariantCulture),
                    int.Parse(args[5], CultureInfo.InvariantCulture)));
            }
            if (args.Length == 5 && args[0] == "--transcode-av-window")
            {
                return await AudiovisualWindowSample.RunAsync(args[1], args[2],
                    long.Parse(args[3], CultureInfo.InvariantCulture), long.Parse(args[4], CultureInfo.InvariantCulture));
            }
            if (args.Length == 5 && args[0] == "--transcode-window")
            {
                return await TranscodeSample.RunWindowAsync(args[1], args[2],
                    long.Parse(args[3], CultureInfo.InvariantCulture), long.Parse(args[4], CultureInfo.InvariantCulture));
            }
            if (args.Length == 4 && args[0] == "--audio-preview")
            {
                return await AudioPreviewSample.RunAsync(args[1], args[2], args[3]);
            }
            if (args.Length == 3 && args[0] == "--transcode")
            {
                return await TranscodeSample.RunAsync(args[1], args[2]);
            }
            if (args.Length == 3 && args[0] == "--preview")
            {
                return await VideoPreviewSample.RunAsync(args[1], args[2]);
            }
            if (args.Length == 2 && args[0] == "--waveform")
            {
                return await AudioWaveformSample.RunAsync(args[1]);
            }
            if (args.Length > 1 || (args.Length == 1 && args[0] != "--self-test"))
            {
                await Console.Error.WriteLineAsync("Usage: Mediana.Sample [--self-test | --prepare-hls input.mp4 output-directory end-ms segment-ms maximum-width | --transcode input.mp4 output.ts | --transcode-window input.mp4 output.ts start-ms end-ms | --transcode-av-window input.mp4 output.ts start-ms end-ms | --preview input output.png | --waveform input | --audio-preview input cover.png waveform.json]");
                return 2;
            }

            MediaEngine engine = new MediaEngineBuilder().Build();
            MediaBufferAllocator allocator = new(1024);
            await using GeneratedPacketSource source = new(allocator);
            using (VideoDecodePipeline pipeline = new(source, new RgbaSampleDecoder(allocator), 0, 8))
            {
                using VideoFrame frame = await pipeline.ReadAsync()
                    ?? throw new InvalidOperationException("The generated frame is missing.");
                using VideoFrame resized = NearestNeighborResize.Apply(frame, 1, 1, allocator);
                if (!resized.Data.Span.SequenceEqual(new byte[] { 255, 0, 0, 255 }))
                {
                    throw new InvalidOperationException("Unexpected resize output.");
                }
                if (await pipeline.ReadAsync() is not null)
                {
                    throw new InvalidOperationException("Unexpected extra output.");
                }
                MediaTimestamp timestamp = new(90_000, new TimeBase(1, 90_000));
                if (timestamp.Rescale(TimeBase.TimeSpanTicks, TimestampRounding.NearestEven).Value != TimeSpan.TicksPerSecond)
                {
                    throw new InvalidOperationException("Unexpected timestamp conversion.");
                }
                await Console.Out.WriteLineAsync("Generated RGBA frame: 2x2 -> 1x1; pixel: FF0000FF; timestamp: 1 second.");
            }

            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Media buffers were not released.");
            }
            await Console.Out.WriteLineAsync(
                $"Foundation checks passed. Registered production containers: {engine.ContainerImplementations.Count}. Live buffer bytes: {allocator.AllocatedBytes}.");
            return 0;
        }

    }
}
