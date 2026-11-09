// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Diagnostics;
using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.OperationComparison
{
    internal static class H264EncodingComparison
    {
        internal static async Task RunAsync(string inputPath, string destination, int quantizer, int bitRate, int keyFrames,
            H264EntropyCoding entropy = H264EntropyCoding.Cavlc)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(bitRate);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(keyFrames);
            using Process process = Process.GetCurrentProcess();
            long allocated = GC.GetTotalAllocatedBytes(true);
            TimeSpan cpu = process.TotalProcessorTime;
            Stopwatch watch = Stopwatch.StartNew();
            MediaBufferAllocator allocator = new(int.MaxValue);
            await using FileStream input = new(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
            Yuv4MpegInput source = await Yuv4MpegInput.OpenAsync(input);
            H264EncoderOptions options = new()
            {
                Quantizer = quantizer,
                EntropyCoding = entropy,
                KeyFrameInterval = TimeSpan.FromSeconds((double)keyFrames * source.Clock.Numerator / source.Clock.Denominator),
                RateControl = bitRate == 0 ? null : new() { BitRate = bitRate, BufferDuration = TimeSpan.FromSeconds(2) }
            };
            int pictures = 0;
            long packetBytes = 0;
            await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, FileOptions.Asynchronous);
            using (H264Encoder encoder = H264Encoder.Create(source.Format, source.Clock, allocator, options))
            {
                await using MpegTsWriter writer = MpegTsWriter.Create(output, [encoder.Stream], allocator);
                while (await source.ReadFrameAsync(allocator) is VideoFrame frame)
                {
                    using (frame)
                    {
                        if (encoder.TrySend(frame) != CodecInputStatus.Accepted)
                        {
                            throw new InvalidOperationException("The drained encoder did not accept a picture.");
                        }
                        using MediaPacket packet = encoder.Receive().Value ?? throw new InvalidDataException("Missing encoded picture.");
                        packetBytes += packet.Data.Length;
                        await writer.WritePacketAsync(packet);
                        pictures++;
                    }
                }
                encoder.CompleteInput();
                if (encoder.Receive().Status != CodecOutputStatus.EndOfStream)
                {
                    throw new InvalidDataException("The encoder did not reach its end.");
                }
                await writer.CompleteAsync();
            }
            await output.FlushAsync();
            if (pictures == 0 || allocator.AllocatedBytes != 0 || input.Position != input.Length)
            {
                throw new InvalidDataException("Encoding left unread input, retained media or no output pictures.");
            }
            watch.Stop();
            cpu = process.TotalProcessorTime - cpu;
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            process.Refresh();
            await Console.Out.WriteLineAsync(FormattableString.Invariant(
                $"pictures={pictures}; packet-bytes={packetBytes}; eof=true; retained-bytes=0; elapsed-ms={watch.Elapsed.TotalMilliseconds:F3}; cpu-ms={cpu.TotalMilliseconds:F3}; allocated-bytes={allocated}; peak-rss-bytes={process.PeakWorkingSet64}"));
        }
    }
}
