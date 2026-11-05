// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    internal static class HlsRenditionTestInput
    {
        private static HlsRenditionOptions Options => new()
        {
            VideoProfiles = [new(96, new() { MotionSearchRange = 0 }), new(48, new() { MotionSearchRange = 0 })],
            TargetDuration = new(500, new(1, 1000))
        };

        internal static async Task VerifyAsync(string root)
        {
            MediaEngine engine = Engine();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, "avc-multislice-cabac.mp4")));
            await using VideoSession session = await engine.OpenVideoAsync(source);
            MediaBufferAllocator allocator = new(32 * 1024 * 1024);
            HlsPackageTestOutput output = new();
            List<HlsEncodingProgress> progress = [];
            HlsEncodingResult result = await HlsRenditionEncoder.EncodeAsync(session, output, Options, allocator, progress: progress.Add);
            Require(result.InputFrames > 0 && result.VideoRenditions == 2 && result.SegmentsPerRendition == 6);
            Require(progress.Count == result.InputFrames + 1 && progress[^1].Completed && progress[^1].ProcessedDuration is MediaDuration duration && new MediaTimestamp(duration.Value, duration.TimeBase).CompareTo(new(3, new(1, 1))) == 0);
            Require(output.Playlists["index.m3u8"].Contains("RESOLUTION=96x80", StringComparison.Ordinal));
            Require(output.Playlists["variant-96/index.m3u8"] == output.Playlists["variant-48/index.m3u8"]);
            Require(output.Segments.Count == 12 && output.Segments.Values.All(segment => !segment.CanWrite && segment.ToArray().Length > 0));
            Require(allocator.AllocatedBytes == 0 && (await source.ReadAtAsync(0, new byte[1]) == 1));
            await session.DisposeAsync();
            Require(session.AllocatedBufferBytes == 0);
        }

        internal static async Task VerifyFailureAsync(string root, bool cancel)
        {
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, "avc-multislice-cabac.mp4")));
            await using VideoSession session = await Engine().OpenVideoAsync(source);
            using CancellationTokenSource cancellation = new();
            HlsPackageTestOutput output = new() { FailOnSegment = !cancel, BeforeSegment = cancel ? cancellation.Cancel : null };
            MediaBufferAllocator allocator = new(32 * 1024 * 1024);
            bool rejected = false;
            try { await HlsRenditionEncoder.EncodeAsync(session, output, Options, allocator, cancellationToken: cancellation.Token); }
            catch (IOException error) when (!cancel && error.Message == "segment-output") { rejected = true; }
            catch (OperationCanceledException) when (cancel) { rejected = true; }
            Require(rejected && allocator.AllocatedBytes == 0 && output.Playlists.Count == 0 && (await source.ReadAtAsync(0, new byte[1]) == 1));
            await session.DisposeAsync();
            Require(session.AllocatedBufferBytes == 0);
        }

        internal static async Task VerifySeparateAudioAsync(string root)
        {
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, "avc-aac.mp4")));
            await using VideoSession session = await Engine().OpenVideoAsync(source, 0);
            MediaBufferAllocator allocator = new(32 * 1024 * 1024);
            MediaEngine audioEngine = new MediaEngineBuilder().AddAudioDecoder(new AacDecoderFactory()).Build();
            await using HlsAacAudioSource audio = await HlsAacAudioSource.OpenAsync(audioEngine, source, 1, [64000, 128000], null, allocator);
            HlsPackageTestOutput output = new();
            HlsRenditionOptions options = Options with
            {
                VideoProfiles = [new(64, new() { MotionSearchRange = 0 }), new(32, new() { MotionSearchRange = 0 })],
                SeparateAudio = true,
                AudioProfileIndices = [1, 0]
            };
            HlsEncodingResult result = await HlsRenditionEncoder.EncodeAsync(session, output, options, allocator, audio);
            Require(result.AudioPackets > 0 && output.Playlists.Count == 5);
            string master = output.Playlists["index.m3u8"];
            Require(master.Contains("AUDIO=\"audio-1\"", StringComparison.Ordinal) && master.Contains("AUDIO=\"audio-0\"", StringComparison.Ordinal));
            Require(output.Playlists.Values.All(playlist => playlist.Contains("#EXTM3U", StringComparison.Ordinal)));
            await audio.DisposeAsync();
            await session.DisposeAsync();
            Require(allocator.AllocatedBytes == 0 && session.AllocatedBufferBytes == 0);
        }

        private static MediaEngine Engine() => new MediaEngineBuilder()
            .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            .AddVideoDecoder(new H264DecoderFactory()).Build();

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("HLS rendition package, progress or resource ownership differs from the public contract."); }
        }
    }
}
