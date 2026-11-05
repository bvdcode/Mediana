// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    internal static class HlsAudioPreparationTestInput
    {
        internal static async Task VerifyAsync(string root)
        {
            await using FileStream file = File.OpenRead(Path.Combine(root, "lc-mono-48.m4a"));
            await using RandomAccessStreamByteSource source = new(file, leaveOpen: true);
            MediaEngine engine = new MediaEngineBuilder().AddAudioDecoder(new AacDecoderFactory()).Build();
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            using WritableMediaBuffer unrelated = allocator.Allocate(16);
            long samples = 0;
            await using (HlsAacPcmSource audio = await HlsAacPcmSource.OpenAsync(engine, source, 0, null, allocator, default))
            {
                while (true)
                {
                    using AudioFrame? frame = await audio.ReadFrameAsync(default);
                    if (frame is null) { break; }
                    samples += frame.SamplesPerChannel;
                }
            }
            if (samples <= 0 || allocator.AllocatedBytes != 16 || !file.CanRead)
            { throw new InvalidDataException("HLS PCM preparation must retain caller resources and release its own buffers."); }
        }
    }
}
