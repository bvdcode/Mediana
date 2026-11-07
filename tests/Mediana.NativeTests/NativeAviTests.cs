// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using Mediana.Codecs.Aac;
using Mediana.Codecs.H264;
using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Pcm;
using Mediana.Containers.Avi;
using Mediana.Containers.MpegTs;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.NativeTests
{
    internal static class NativeAviTests
    {
        public static async Task RunAsync()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Avi", "mjpeg-pcm.avi");
            using FileStream input = File.OpenRead(path);
            await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new AviReaderFactory())
                .AddVideoDecoder(new MjpegDecoderFactory()).AddAudioDecoder(new PcmDecoderFactory())
                .AddContainerWriter(new MpegTsWriterFactory()).Build();
            MediaBufferAllocator allocator = new(16 * 1024 * 1024, MemoryPool<byte>.Shared);
            using MemoryStream output = new();
            MediaProcessingResult result = await engine.ProcessAsync(source, output, "mpegts",
                [new VideoEncodingRequest(0, 0, new H264EncoderFactory(), new VideoPreparationOptions { RgbConversion = new(), MaximumWidth = 32 }),
                 new AudioEncodingRequest(1, 1, new AacEncoderFactory())], allocator,
                new() { Selection = new MediaTimeRange(new(1000, new(1, 1000)), new(1500, new(1, 1000))) });
            if (result.Tracks[0].Frames != 4 || result.Tracks[1].SamplesPerChannel != 24000 ||
                output.Length == 0 || output.Length % 188 != 0 || allocator.AllocatedBytes != 0 || !input.CanRead)
            { throw new InvalidDataException("The AVI audiovisual interval changed counts, transport or borrowed storage ownership."); }
            Console.WriteLine("PASS AVI: indexed MJPEG/PCM interval, independent decoder restart, AVC/AAC output and pooled buffer release.");
        }
    }
}
