// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Codecs.Vp9;
using Mediana.Containers.Matroska;
using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Execution;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class VideoTranscodeTestInput
    {
        internal static async Task VerifyAsync(string root)
        {
            await VerifyAdaptiveAsync(root);
            await VerifyFractionalTailAsync(root, 1001);
            await VerifyFractionalTailAsync(root, 1101);
            await VerifyFractionalTailAsync(root, 1001, true);
        }

        internal static async Task VerifyAdaptiveAsync(string root)
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(root, "Vp9", "resize.webm"));
            H264EncoderFactory encoder = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory())
                .AddVideoDecoder(new Vp9DecoderFactory()).AddVideoEncoder(encoder)
                .AddContainerWriter(new MpegTsWriterFactory()).Build();
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(bytes, 7);
            MediaDurationAccumulator expected = new();
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator))
            {
                Require(reader.GetPresentationUnitCount(0) == 20, "The adaptive VP9 sequence requires 20 displayed pictures.");
                for (long index = 0; index < reader.GetPresentationUnitCount(0); index++)
                { expected.Add(reader.GetPresentationUnit(0, index).Duration); }
                Require(reader.GetPresentationUnit(0, 19).Duration == new MediaDuration(83_333_333, new(1, 1_000_000_000)),
                    "The VP9 final display duration lost its fractional clock.");
            }
            await VerifyOutputAsync(engine, source, allocator, encoder, 20, expected.Duration!.Value);
        }

        internal static async Task VerifyFractionalTailAsync(string root, int milliseconds, bool rateControl = false)
        {
            byte[] bytes = Mp4EditTestData.Replace(await File.ReadAllBytesAsync(Path.Combine(root, "Mp4", "avc-tail.mov")),
                false, (milliseconds, 0, 65536));
            H264EncoderOptions? encoderOptions = null;
            if (rateControl) { encoderOptions = new() { RateControl = new() { BitRate = 64000 } }; }
            H264EncoderFactory encoder = new(encoderOptions);
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddVideoDecoder(new H264DecoderFactory()).AddVideoEncoder(encoder)
                .AddContainerWriter(new MpegTsWriterFactory()).Build();
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(bytes, 7);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                Require(reader.Streams[0].TimeBase == new TimeBase(1, 10240), "The movie fixture's coded stream clock changed.");
                Require(reader.GetPresentationRange(0)!.Value.End == new MediaTimestamp(milliseconds, new(1, 1000)),
                    "The fractional movie boundary was rounded.");
            }
            MediaDuration duration = new(milliseconds, new(1, 1000));
            await VerifyOutputAsync(engine, source, allocator, encoder, 6, duration, duration);
        }

        private static async Task VerifyOutputAsync(MediaEngine engine, IByteSource source, MediaBufferAllocator allocator,
            H264EncoderFactory encoder, long frames, MediaDuration duration, MediaDuration? declaredDuration = null)
        {
            using MemoryStream output = new();
            List<MediaOutputProgress> updates = [];
            VideoTranscodeOptions options = new() { MaximumWidth = 32 };
            MediaOperationProgress result = await engine.TranscodeVideoAsync(source, output, "h264", "mpegts", allocator, options, updates.Add);
            Require(result.Frames == frames && result.IsComplete && result.ProcessedDuration is not null,
                "Convenience video encoding lost input frames, progress duration or completion.");
            MediaDuration processedDuration = result.ProcessedDuration!.Value;
            Require(new MediaTimestamp(duration.Value, duration.TimeBase) == new MediaTimestamp(processedDuration.Value, processedDuration.TimeBase),
                "Convenience progress lost exact input display duration.");
            Require(allocator.AllocatedBytes == 0 && updates.Count != 0, "Video encoding retained storage or omitted progress.");
            for (int index = 0; index < updates.Count; index++)
            {
                Require(declaredDuration is null || updates[index].TotalDuration == declaredDuration,
                    "Progress uses coded duration instead of the selected movie presentation range.");
                Require(updates[index].IsComplete == (index == updates.Count - 1), "Video encoding reported completion before finalization.");
                if (index != 0) { Require(updates[index].BytesWritten >= updates[index - 1].BytesWritten, "Output progress moved backwards."); }
            }
            Require(updates[^1].ProcessedDuration == result.ProcessedDuration && updates[^1].BytesWritten == output.Length,
                "Final progress differs from encoded output.");
            using MemoryStream reference = new();
            MediaProcessingResult processed = await engine.ProcessAsync(source, reference, "mpegts",
                [new VideoEncodingRequest(0, 0, encoder, options)], allocator);
            Require(output.ToArray().AsSpan().SequenceEqual(reference.ToArray()) && result.Frames == processed.Tracks[0].Frames &&
                result.Packets == processed.Packets && result.PayloadBytes == processed.Tracks[0].PayloadBytes,
                "Convenience encoding differs from the common processing pipeline.");
            Require(allocator.AllocatedBytes == 0 && output.CanWrite, "Encoding retained allocations or disposed the borrowed output.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) { throw new InvalidDataException(message); }
        }
    }
}
