// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using Mediana.Codecs.H264;
using Mediana.Codecs.Vp8;
using Mediana.Containers.Matroska;
using Mediana.Containers.MpegTs;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class Vp8ContainerTestInput
    {
        internal static async Task VerifyAsync(string root)
        {
            foreach ((string name, int count) in new[] { ("motion", 24), ("altref", 72) })
            {
                MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory())
                    .AddVideoDecoder(new Vp8DecoderFactory()).AddContainerWriter(new MpegTsWriterFactory()).Build();
                MediaBufferAllocator allocator = new(16 * 1024 * 1024, System.Buffers.MemoryPool<byte>.Shared);
                await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".webm")));
                byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + ".yuv"));
                Dictionary<MediaTimestamp, string> hashes = [];
                using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                int offset = 0;
                await using (VideoSession session = await engine.OpenVideoAsync(source, allocator, new(), null))
                {
                    if (session.Stream.CodecId != "vp8") { throw new InvalidDataException("WebM did not expose a VP8 stream."); }
                    while (await session.ReadFrameAsync() is VideoFrame frame)
                    {
                        using (frame)
                        {
                            for (int plane = 0; plane < frame.Planes.Count; plane++)
                            {
                                for (int row = 0; row < frame.Planes[plane].Height; row++)
                                {
                                    ReadOnlySpan<byte> pixels = frame.GetRow(plane, row);
                                    if (!pixels.SequenceEqual(reference.AsSpan(offset, pixels.Length)))
                                    { throw new InvalidDataException("WebM VP8 pixels differ from independent reconstruction."); }
                                    hash.AppendData(pixels); offset += pixels.Length;
                                }
                            }
                            hashes.Add(frame.Timestamp ?? throw new InvalidDataException("Missing WebM presentation time."), Convert.ToHexString(hash.GetHashAndReset()));
                        }
                    }
                    if (hashes.Count != count || offset != reference.Length) { throw new InvalidDataException("WebM visible picture count differs."); }
                    foreach (int milliseconds in new[] { 1500, 0, 500, 1500 })
                    {
                        MediaTimestamp target = new(milliseconds, new(1, 1000));
                        await session.SeekAsync(target);
                        using VideoFrame frame = await session.ReadFrameAtOrAfterAsync(target) ?? throw new InvalidDataException("VP8 seek lost its picture.");
                        for (int plane = 0; plane < frame.Planes.Count; plane++)
                        { for (int row = 0; row < frame.Planes[plane].Height; row++) { hash.AppendData(frame.GetRow(plane, row)); } }
                        if (frame.Timestamp is not MediaTimestamp timestamp || !hashes.TryGetValue(timestamp, out string? expected) ||
                            expected != Convert.ToHexString(hash.GetHashAndReset()))
                        { throw new InvalidDataException("VP8 indexed seek changed reconstructed pixels."); }
                    }
                }
                if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("VP8 decoding retained source or reference storage."); }
                using MemoryStream output = new();
                MediaProcessingResult result = await engine.ProcessAsync(source, output, "mpegts",
                    [new VideoEncodingRequest(0, 0, new H264EncoderFactory(), new VideoPreparationOptions { MaximumWidth = 32 })], allocator);
                if (result.Tracks[0].Frames != count || output.Length == 0 || allocator.AllocatedBytes != 0)
                { throw new InvalidDataException("WebM VP8 to AVC changed picture counts or retained storage."); }
            }
        }
    }
}
