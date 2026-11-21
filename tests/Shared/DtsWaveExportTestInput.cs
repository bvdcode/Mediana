// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs.Dts;
using Mediana.Codecs.Pcm;
using Mediana.Containers.Matroska;
using Mediana.Containers.Wave;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class DtsWaveExportTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, "surround.pcm"));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory())
                .AddContainer(new WaveReaderFactory()).AddAudioDecoder(new DtsDecoderFactory())
                .AddAudioDecoder(new PcmDecoderFactory()).Build();
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, "surround.mkv")));
            using MemoryStream output = new();
            PcmExportResult result = await engine.ExportPcmAsync(source, output, new WavePcmWriterFactory(), allocator);
            string[] channels = ["front-left", "front-right", "front-center", "low-frequency", "side-left", "side-right"];
            if (!result.Format.Channels.SequenceEqual(channels) || allocator.AllocatedBytes != 0)
            { throw new InvalidDataException("WAVE export did not select its speaker order or release storage."); }
            output.Position = 0;
            await using RandomAccessStreamByteSource wave = new(output, leaveOpen: true);
            await using AudioSession session = await engine.OpenAudioAsync(wave, allocator, new(), null);
            int position = 0;
            int[] codedChannels = [1, 2, 0, 5, 3, 4];
            while (await session.ReadFrameAsync() is AudioFrame frame)
            {
                using (frame)
                {
                    if (!frame.Format.Channels.SequenceEqual(channels))
                    { throw new InvalidDataException("The WAVE speaker mask lost channel identities."); }
                    ReadOnlySpan<short> actual = MemoryMarshal.Cast<byte, short>(frame.GetPlane(0).Span);
                    ReadOnlySpan<float> expected = MemoryMarshal.Cast<byte, float>(reference);
                    for (int i = 0; i < actual.Length; i++)
                    {
                        int sample = (position + i) / 6;
                        int channel = codedChannels[i % 6];
                        double value = Math.Clamp(Math.Round(Math.Clamp(expected[sample * 6 + channel], -1, 1) * 32768d), short.MinValue, short.MaxValue);
                        if (Math.Abs(actual[i] - value) > 1)
                        { throw new InvalidDataException("WAVE PCM channels differ from the independent DTS reference."); }
                    }
                    position += actual.Length;
                }
            }
            await session.DisposeAsync();
            if (position * sizeof(float) != reference.Length || allocator.AllocatedBytes != 0)
            { throw new InvalidDataException("The DTS to WAVE conversion lost samples or retained storage."); }
        }
    }
}
