// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core.Memory;
using Mediana.Execution;
using Mediana.IO;
using Mediana.Processing;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal static class VideoTranscodeCommand
    {
        private const long BufferBudgetBytes = 128 * 1024 * 1024;

        public static async Task<int> RunAsync(string input, CommandOutputFile outputFile, int? streamIndex, H264EncoderOptions options,
            int? maximumWidth, int? maximumHeight, bool videoOnly, ILogger logger, CancellationToken cancellationToken)
        {
            MediaEngine engine = ContainerEngineFactory.CreateVideoBuilder(Mp4TimestampMode.LinearMovie)
                .AddVideoEncoder(new H264EncoderFactory(options)).AddContainerWriter(new MpegTsWriterFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            MediaProgress progress = new(logger, TimeProvider.System);
            MediaOperationProgress? result = null;
            await outputFile.WriteAsync(async destination =>
            {
                result = await engine.TranscodeVideoAsync(source, destination, "h264", "mpegts", allocator, new()
                {
                    StreamIndex = streamIndex,
                    MaximumWidth = maximumWidth,
                    MaximumHeight = maximumHeight,
                    OmitAudio = videoOnly,
                    ColorResolver = VideoColorPolicy.ResolveForEncoding,
                    WarningHandler = message => logger.LogWarning("{Warning}", message),
                    RgbConversion = new()
                }, progress.Report, cancellationToken);
            }, cancellationToken);
            if (allocator.AllocatedBytes != 0) { throw new InvalidOperationException("Video encoding buffers were not released."); }
            string quality = options.RateControl is H264RateControlOptions rate
                ? $"variable packet rate {rate.BitRate} bits/s; initial QP {options.Quantizer}" : $"QP {options.Quantizer}";
            await Console.Out.WriteLineAsync($"MPEG-TS video encoding: {result!.Frames} frames; Baseline CAVLC I/P; {quality}; video only. Live buffer bytes: 0.");
            return 0;
        }
    }
}
