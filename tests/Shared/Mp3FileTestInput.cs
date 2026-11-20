// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Codecs.Mp3;
using Mediana.Codecs.Pcm;
using Mediana.Containers.Avi;
using Mediana.Containers.Mp3;
using Mediana.Containers.Mp4;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class Mp3FileTestInput
    {
        public static async Task VerifyAsync(string inputPath, long? expectedSamples = null)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new AviReaderFactory())
                .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddContainer(new Mp3ReaderFactory()).AddAudioDecoder(new PcmDecoderFactory())
                .AddAudioDecoder(new AacDecoderFactory()).AddAudioDecoder(new Mp3DecoderFactory())
                .AddAudioEncoder(new Mp3EncoderFactory()).AddContainerWriter(new Mp3WriterFactory()).Build();
            MediaBufferAllocator allocator = new(8 * 1024 * 1024);
            using FileStream input = File.OpenRead(inputPath);
            await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
            using MemoryStream output = new();
            AudioTranscodeResult result = await engine.TranscodeAudioAsync(source, output, "mp3", "mp3", allocator);
            if (result.SamplesPerChannel <= 0 || (expectedSamples is long expected && result.SamplesPerChannel != expected) ||
                allocator.AllocatedBytes != 0 || !input.CanRead || !output.CanWrite)
            { throw new InvalidDataException("MP3 file conversion changed its sample count or retained input/output resources."); }
            output.Position = 0;
            await using RandomAccessStreamByteSource encoded = new(output, leaveOpen: true);
            long samples = 0;
            await using (PreparedAudioSource decoded = await PreparedAudioSource.OpenAsync(engine, encoded, allocator))
            {
                while (true)
                {
                    using AudioFrame? frame = await decoded.ReadFrameAsync();
                    if (frame is null) { break; }
                    samples += frame.SamplesPerChannel;
                }
            }
            if (samples != result.SamplesPerChannel || allocator.AllocatedBytes != 0 || !output.CanRead)
            { throw new InvalidDataException("The MP3 file did not preserve its declared audible sample count and borrowed storage."); }
        }
    }
}
