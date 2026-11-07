// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Containers;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;
using Mediana.Streaming.Hls;

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
                await VerifyHlsAsync(reader, allocator, baseline, false);
                await VerifyHlsAsync(reader, allocator, baseline, true);
            }
            if (allocator.AllocatedBytes != 0 || !input.CanRead)
            { throw new InvalidOperationException("Audiovisual processing retained storage or closed the borrowed source."); }
            Console.WriteLine("PASS audiovisual interval processing: scaled video, exact PCM sample selection, encoder priming/padding, repeatable muxing and released ownership.");
        }

        private static async Task VerifyHlsAsync(Mp4Reader reader, MediaBufferAllocator allocator, long baseline, bool encodeAudio)
        {
            VideoFormat format = new(64, 48, VideoPixelFormat.Yuv420P8,
                new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Left), new(1, 1));
            await using AvcHlsTranscodeSession session = AvcHlsTranscodeSession.Create(reader, 0, new H264DecoderFactory(), format,
                new MediaDuration(1, new(1, 1)), allocator, preparation: new() { MaximumWidth = 32 },
                copyAudioStreamIndex: encodeAudio ? null : 1,
                audioEncoding: encodeAudio ? new(1, new AacDecoderFactory(),
                    new AudioFormat(48000, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, "front-center"),
                    ((IContainerPresentationReader)reader).GetPresentationRange(1)!.Value) : null);
            using MemoryStream initialization = new();
            await session.WriteInitializationAsync(initialization);
            byte[]? previous = null;
            foreach (int number in new[] { 11, 0, 11 })
            {
                using MemoryStream output = new();
                HlsMediaSegment segment = await session.WriteSegmentAsync(number, output);
                byte[] bytes = output.ToArray();
                if (segment.ByteLength != bytes.Length || bytes.Length == 0 || session.Streams.Count != 2 || allocator.AllocatedBytes != baseline)
                { throw new InvalidDataException("An audiovisual HLS request changed its declarations, output length or resource ownership."); }
                if (number == 11)
                {
                    if (previous is not null && !previous.AsSpan().SequenceEqual(bytes))
                    { throw new InvalidDataException("Repeated cold audiovisual HLS requests changed their fragment bytes."); }
                    previous = bytes;
                }
            }
            Console.WriteLine($"PASS audiovisual HLS intervals: shared processing, scaling, AAC {(encodeAudio ? "encoding" : "copy")} and repeated cold fragments.");
        }
    }
}
