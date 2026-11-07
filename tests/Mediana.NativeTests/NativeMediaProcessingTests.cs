// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.NativeTests
{
    internal static class NativeMediaProcessingTests
    {
        public static async Task RunAsync(string root)
        {
            MediaEngine engine = new MediaEngineBuilder().AddVideoDecoder(new H264DecoderFactory())
                .AddAudioDecoder(new AacDecoderFactory()).Build();
            MediaBufferAllocator allocator = new(8 * 1024 * 1024);
            using FileStream input = File.OpenRead(Path.Combine(root, "avc-aac-on-demand.mp4"));
            await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                long baseline = allocator.AllocatedBytes;
                byte[]? previous = null;
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    await using (MediaProcessingSession session = await engine.OpenProcessingAsync(reader,
                        [new VideoEncodingRequest(0, 7, new H264EncoderFactory(),
                            new VideoPreparationOptions { MaximumWidth = 32, MaximumHeight = 32 }, new(1, 5)),
                         new AudioEncodingRequest(1, 13, new AacEncoderFactory())], allocator,
                         new() { Selection = new(new(11, new(1, 1)), new(12, new(1, 1))) }))
                    {
                        using MemoryStream output = new();
                        await using MpegTsWriter writer = MpegTsWriter.Create(output, session.Streams, allocator);
                        MediaProcessingResult result = await session.WriteAsync(writer);
                        if (result.Tracks.Count != 2 || result.Tracks[0].Frames != 5 || result.Tracks[1].SamplesPerChannel != 48000 ||
                            result.Tracks[1].PrimingSamples != 1024 || result.Tracks[1].PaddingSamples != 128 || output.Length % 188 != 0)
                        { throw new InvalidDataException("The independent audiovisual operation changed selected frames, PCM samples or encoder timing."); }
                        byte[] bytes = output.ToArray();
                        if (previous is not null && !previous.AsSpan().SequenceEqual(bytes))
                        { throw new InvalidDataException("Repeated independent audiovisual requests changed transport output."); }
                        previous = bytes;
                    }
                    if (allocator.AllocatedBytes != baseline)
                    { throw new InvalidOperationException("Audiovisual processing retained codec or presentation buffers."); }
                }
            }
            if (allocator.AllocatedBytes != 0 || !input.CanRead)
            { throw new InvalidOperationException("Audiovisual processing retained storage or closed the borrowed source."); }
            Console.WriteLine("PASS audiovisual interval processing: scaled video, exact PCM sample selection, encoder priming/padding, repeatable muxing and released ownership.");
        }
    }
}
