// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using System.Security.Cryptography;
using Mediana.Codecs.Vp9;
using Mediana.Codecs.H264;
using Mediana.Containers.MpegTs;
using Mediana.Containers.Matroska;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class Vp9ContainerTestInput
    {
        internal static async Task VerifyAsync(string root, string name)
        {
            string[] expected = (await File.ReadAllLinesAsync(Path.Combine(root, name + ".sha256"))).Where(line => !line.StartsWith('#')).Select(line => line.Split(',')[^1].Trim().ToUpperInvariant()).ToArray();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory()).AddVideoDecoder(new Vp9DecoderFactory()).AddVideoDecoder(new H264DecoderFactory()).AddContainerWriter(new MpegTsWriterFactory()).Build();
            MediaBufferAllocator allocator = new(128 * 1024 * 1024, MemoryPool<byte>.Shared);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".webm")));
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Dictionary<MediaTimestamp, string> pictures = [];
            await using (VideoSession session = await engine.OpenVideoAsync(source, allocator, new(), null))
            {
                if (session.Stream.CodecId != "vp9") { throw new InvalidDataException("WebM did not expose VP9 video."); }
                while (await session.ReadFrameAsync() is VideoFrame frame)
                {
                    using (frame)
                    {
                        string digest = Hash(frame, hash);
                        if (expected[pictures.Count] != digest) { throw new InvalidDataException("WebM VP9 differs from independent reconstruction."); }
                        pictures.Add(frame.Timestamp ?? throw new InvalidDataException("Missing VP9 presentation time."), digest);
                        if (pictures.Count == 1)
                        {
                            using VideoFrame preview = SrgbPreview.FitWithColorPolicy(frame, SdrPreviewColorPolicy.Resolve, VideoDisplayMatrix.Identity, 32, 32, allocator);
                            if (preview.Format.Width > 32 || preview.Format.Height > 32) { throw new InvalidDataException("VP9 preview exceeded its dimensions."); }
                        }
                    }
                }
                if (pictures.Count != expected.Length) { throw new InvalidDataException("WebM VP9 picture count differs."); }
                foreach (int milliseconds in new[] { 500, 0, 250, 500 })
                {
                    MediaTimestamp target = new(milliseconds, new(1, 1000)); await session.SeekAsync(target);
                    using VideoFrame frame = await session.ReadFrameAtOrAfterAsync(target) ?? throw new InvalidDataException("VP9 indexed seek lost its picture.");
                    if (frame.Timestamp is not MediaTimestamp timestamp || !pictures.TryGetValue(timestamp, out string? digest) || digest != Hash(frame, hash))
                    { throw new InvalidDataException("VP9 indexed seek changed reconstructed pixels."); }
                }
            }
            using MemoryStream output = new();
            MediaProcessingResult encoded = await engine.ProcessAsync(source, output, "mpegts",
                [new VideoEncodingRequest(0, 0, new H264EncoderFactory(), new VideoPreparationOptions
                    { OutputPixelFormat = VideoPixelFormat.Yuv420P8, MaximumWidth = 32, RgbConversion = new() })], allocator);
            if (encoded.Tracks[0].Frames != expected.Length || output.Length == 0)
            { throw new InvalidDataException("VP9 to AVC encoding changed the picture count."); }
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("VP9 session retained frame or reference storage."); }
        }

        private static string Hash(VideoFrame frame, IncrementalHash hash)
        {
            for (int plane = 0; plane < frame.Planes.Count; plane++)
            { for (int y = 0; y < frame.Planes[plane].Height; y++) { hash.AppendData(frame.GetRow(plane, y)); } }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
    }
}
