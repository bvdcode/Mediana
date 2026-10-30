// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Pcm;
using Mediana.Containers.Aiff;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class AiffPcmReferenceTestInput
    {
        public static readonly string[] Names = ["s8-mono", "s16-stereo", "s24-stereo", "s32-stereo", "f32-stereo", "f64-stereo", "s16le-stereo"];

        public static async Task VerifyAsync(string root, string name)
        {
            (int rate, AudioSampleFormat format, string[] channels) = name switch
            {
                "s8-mono" => (8000, AudioSampleFormat.Unsigned8, new[] { "mono" }),
                "s16-stereo" or "s16le-stereo" => (44100, AudioSampleFormat.Signed16, new[] { "front-left", "front-right" }),
                "s24-stereo" or "s32-stereo" => (48000, AudioSampleFormat.Signed32, new[] { "front-left", "front-right" }),
                "f32-stereo" => (44100, AudioSampleFormat.Float32, new[] { "front-left", "front-right" }),
                "f64-stereo" => (48000, AudioSampleFormat.Float64, new[] { "front-left", "front-right" }),
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };
            string referenceName = name switch
            {
                "s8-mono" => "u8-mono",
                "s16le-stereo" => "s16-stereo",
                "s16-stereo" or "s24-stereo" or "s32-stereo" or "f32-stereo" or "f64-stereo" => name,
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "..", "Wave", referenceName + ".pcm"));
            AudioFormat expectedFormat = new(rate, format, AudioStorageLayout.Interleaved, channels);
            int samples = expected.Length / (expectedFormat.BytesPerSample * channels.Length);
            string inputPath = Path.Combine(root, name + ".aiff");
            MediaBufferAllocator allocator = new(16384);
            await using (StreamByteSource source = new(File.OpenRead(inputPath)))
            await using (AiffReader reader = await AiffReader.OpenAsync(source, allocator))
            {
                if (reader.SamplesPerChannel != samples || reader.Duration != new MediaDuration(samples, new TimeBase(1, rate))
                    || reader.Configuration.Format.SampleFormat != format || !reader.Configuration.Format.Channels.SequenceEqual(channels))
                {
                    throw new InvalidDataException("AIFF metadata does not match the independent PCM reference.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("AIFF metadata reading retained buffer storage.");
            }
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new AiffReaderFactory(new AiffReadOptions { SamplesPerPacket = 127 }))
                .AddAudioDecoder(new AiffPcmDecoderFactory()).Build();
            await PcmReferenceTestInput.VerifyAsync(engine, inputPath, expected, expectedFormat);
        }
    }
}
