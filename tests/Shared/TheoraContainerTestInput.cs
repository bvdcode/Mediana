// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using System.Security.Cryptography;
using Mediana.Codecs.Theora;
using Mediana.Codecs.H264;
using Mediana.Containers.MpegTs;
using Mediana.Containers.Ogg;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class TheoraContainerTestInput
    {
        internal static async Task VerifyAsync(string root, string name)
        {
            string[] expected = (await File.ReadAllLinesAsync(Path.Combine(root, name + ".framehash"))).Where(line => !line.StartsWith('#')).Select(line => line.Split(',')[^1].Trim().ToUpperInvariant()).ToArray();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new OggReaderFactory()).AddVideoDecoder(new TheoraDecoderFactory()).AddVideoDecoder(new H264DecoderFactory()).AddContainerWriter(new MpegTsWriterFactory()).Build();
            MediaBufferAllocator allocator = new(128 * 1024 * 1024, MemoryPool<byte>.Shared);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".ogv")));
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Dictionary<MediaTimestamp, string> pictures = [];
            await using (VideoSession session = await engine.OpenVideoAsync(source, allocator, new(), null))
            {
                if (session.Stream.CodecId != "theora") { throw new InvalidDataException("Ogg did not expose Theora video."); }
                while (await session.ReadFrameAsync() is VideoFrame frame)
                {
                    using (frame)
                    {
                        string digest = Hash(frame, hash);
                        if (expected[pictures.Count] != digest) { throw new InvalidDataException("Ogg Theora differs from independent reconstruction."); }
                        pictures.Add(frame.Timestamp ?? throw new InvalidDataException("Missing Theora presentation time."), digest);
                        if (pictures.Count == 1)
                        {
                            using VideoFrame preview = SrgbPreview.FitWithColorPolicy(frame, SdrPreviewColorPolicy.Resolve, VideoDisplayMatrix.Identity, 32, 32, allocator);
                            if (preview.Format.Width > 32 || preview.Format.Height > 32) { throw new InvalidDataException("Theora preview exceeded its dimensions."); }
                        }
                    }
                }
                if (pictures.Count != expected.Length) { throw new InvalidDataException("Ogg Theora picture count differs."); }
                MediaTimestamp[] positions = pictures.Keys.ToArray();
                foreach (MediaTimestamp target in new[] { positions[^1], positions[0], positions[positions.Length / 2], positions[^1] })
                {
                    await session.SeekAsync(target);
                    using VideoFrame frame = await session.ReadFrameAtOrAfterAsync(target) ?? throw new InvalidDataException("Theora indexed seek lost its picture.");
                    if (frame.Timestamp is not MediaTimestamp timestamp || !pictures.TryGetValue(timestamp, out string? digest) || digest != Hash(frame, hash))
                    { throw new InvalidDataException("Theora indexed seek changed reconstructed pixels."); }
                }
            }
            using MemoryStream output = new();
            MediaProcessingResult encoded = await engine.ProcessAsync(source, output, "mpegts",
                [new VideoEncodingRequest(0, 0, new H264EncoderFactory(), new VideoPreparationOptions
                    { OutputPixelFormat = VideoPixelFormat.Yuv420P8, MaximumWidth = 32, RgbConversion = new() })], allocator);
            if (encoded.Tracks[0].Frames != expected.Length || output.Length == 0)
            { throw new InvalidDataException("Theora to AVC encoding changed the picture count."); }
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("Theora session retained frame or reference storage."); }
        }

        private static string Hash(VideoFrame frame, IncrementalHash hash)
        {
            for (int plane = 0; plane < frame.Planes.Count; plane++)
            { for (int y = 0; y < frame.Planes[plane].Height; y++) { hash.AppendData(frame.GetRow(plane, y)); } }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
    }
}
