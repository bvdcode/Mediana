// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Codecs.Jpeg;
using Mediana.Containers.Matroska;
using Mediana.Containers.MpegTs;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.NativeTests
{
    internal static class NativeMjpegTests
    {
        public static async Task RunAsync()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Mjpeg", "mjpeg-420.mkv");
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory())
                .AddVideoDecoder(new MjpegDecoderFactory()).AddContainerWriter(new MpegTsWriterFactory()).Build();
            MediaBufferAllocator allocator = new(8 * 1024 * 1024, System.Buffers.MemoryPool<byte>.Shared);
            using FileStream input = File.OpenRead(path);
            await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
            using MemoryStream output = new();
            MediaProcessingResult result = await engine.ProcessAsync(source, output, "mpegts",
                [new VideoEncodingRequest(0, 0, new H264EncoderFactory(),
                    new VideoPreparationOptions { RgbConversion = new(), MaximumWidth = 32 })], allocator);
            if (result.Tracks.Count != 1 || result.Tracks[0].Frames != 12 || allocator.AllocatedBytes != 0 ||
                !input.CanRead || output.Length == 0 || output.Length % 188 != 0)
            { throw new InvalidDataException("The MJPEG video operation changed picture counts, output transport or borrowed ownership."); }
            await using VideoSession video = await engine.OpenVideoAsync(source, allocator, new(), null);
            int count = 0;
            while (await video.ReadFrameAsync() is VideoFrame frame)
            {
                using (frame)
                {
                    if (frame.Format.Width != 64 || frame.Format.Height != 48 || frame.Format.PixelFormat != VideoPixelFormat.Rgba32)
                    { throw new InvalidDataException("The MJPEG decoder changed the visible RGB picture format."); }
                }
                count++;
            }
            if (count != 12) { throw new InvalidDataException("The MJPEG decoder lost pictures after an independent processing operation."); }
            Console.WriteLine("PASS MJPEG video: complete Matroska decoding, shared opaque RGB preparation, AVC encoding and pooled buffer release.");
        }
    }
}
