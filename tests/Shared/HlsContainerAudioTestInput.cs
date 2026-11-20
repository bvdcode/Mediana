// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Pcm;
using Mediana.Containers.Avi;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;
using Mediana.Processing.Audio;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    internal static class HlsContainerAudioTestInput
    {
        internal static async Task VerifyAsync(string root)
        {
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, "mjpeg-pcm.avi")));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new AviReaderFactory())
                .AddAudioDecoder(new PcmDecoderFactory()).Build();
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            using WritableMediaBuffer unrelated = allocator.Allocate(16);
            long samples = 0;
            await using (HlsAacPcmSource pcm = await HlsAacPcmSource.OpenAsync(engine, source, 1, null, allocator))
            {
                await using AudioSession reference = await engine.OpenAudioAsync(source, 1);
                while (await pcm.ReadFrameAsync(default) is AudioFrame frame)
                {
                    using (frame)
                    using (AudioFrame original = await reference.ReadFrameAsync() ?? throw new InvalidDataException("Missing AVI PCM reference."))
                    using (AudioFrame converted = AudioSampleConverter.ConvertOrRetain(original,
                        AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, allocator))
                    {
                        if (frame.Timestamp != converted.Timestamp || !frame.GetPlane(0).Span.SequenceEqual(converted.GetPlane(0).Span))
                        { throw new InvalidDataException("HLS preparation changed AVI PCM samples or timestamps."); }
                        samples += frame.SamplesPerChannel;
                    }
                }
                using AudioFrame? tail = await reference.ReadFrameAsync();
                if (tail is not null || samples != 72000) { throw new InvalidDataException("HLS preparation changed the AVI sample count."); }
            }
            await using (HlsAacAudioSource audio = await HlsAacAudioSource.OpenAsync(engine, source, 1, [64000, 128000], null, allocator))
            {
                await audio.WriteBeforeAsync(null, [], default);
                if (audio.InputSamplesPerChannel != 72000 || audio.PacketsWritten != 144 || audio.PaddingSamples != 704)
                { throw new InvalidDataException("AVI AAC encoding changed its input count, priming or padding."); }
            }
            if (allocator.AllocatedBytes != 16) { throw new InvalidDataException("HLS audio did not release its owned buffers."); }
        }
    }
}
