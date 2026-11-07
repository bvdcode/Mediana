// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Codecs.Opus;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    internal static class OpusSilkPcmTestInput
    {
        public static int Verify(string root)
        {
            using GZipStream stream = new(File.OpenRead(Path.Combine(root, "decoded-silk.bin.gz")), CompressionMode.Decompress);
            using BinaryReader reader = new(stream);
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSSLP1"u8))
            {
                throw new InvalidDataException("The SILK PCM reference header is invalid.");
            }
            MediaBufferAllocator allocator = new(32 * 1024);
            Span<float> pcm = stackalloc float[5761];
            Span<Range> ranges = stackalloc Range[48];
            int groups = reader.ReadInt32(), frames = 0;
            for (int group = 0; group < groups; group++)
            {
                int rate = reader.ReadInt32(), channels = reader.ReadInt32(), count = reader.ReadInt32();
                using (SilkFrameDecoder decoder = new(rate, channels, allocator))
                {
                    for (int frame = 0; frame < count; frame++)
                    {
                        byte[] packet = reader.ReadBytes(reader.ReadInt32());
                        int expectedSamples = reader.ReadInt32(); uint expectedRange = reader.ReadUInt32();
                        OpusPacketInfo layout = OpusPacketInfo.ReadFrames(packet, ranges);
                        OpusPacketConfiguration configuration = OpusPacketConfiguration.FromToc(packet[0]);
                        pcm.Fill(float.NaN);
                        int decoded = 0;
                        for (int at = 0; at < layout.FrameCount; at++)
                        {
                            decoded += decoder.Decode(packet.AsSpan()[ranges[at]], configuration, pcm[(decoded * channels)..]);
                        }
                        if (decoded != expectedSamples || decoder.FinalRange != expectedRange)
                        {
                            throw new InvalidDataException($"SILK stream {group}, frame {frame}, count/range differs: {decoded}, {decoder.FinalRange} / {expectedSamples}, {expectedRange}.");
                        }
                        for (int at = 0; at < decoded * channels; at++)
                        {
                            float expected = reader.ReadInt16() * (1f / 32768);
                            if (pcm[at] != expected)
                            {
                                throw new InvalidDataException($"SILK stream {group}, frame {frame}, sample {at}, differs: {pcm[at]} / {expected}.");
                            }
                        }
                        foreach (float value in pcm[(decoded * channels)..])
                        {
                            if (!float.IsNaN(value)) { throw new InvalidDataException("SILK decoding overwrote unused PCM storage."); }
                        }
                        frames++;
                    }
                }
                if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("The SILK decoder retained its working allocation."); }
            }
            if (stream.ReadByte() != -1) { throw new InvalidDataException("The SILK PCM reference has trailing bytes."); }
            return frames;
        }
    }
}
