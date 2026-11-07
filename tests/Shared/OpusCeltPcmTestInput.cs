// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    internal static class OpusCeltPcmTestInput
    {
        public static int Verify(string root, string filename = "decoded-celt.bin")
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, filename)));
            ReadOnlySpan<byte> magic = filename switch
            {
                "decoded-celt.bin" => "OPUSPCM1"u8,
                "decoded-hybrid.bin" => "OPUSHYB1"u8,
                "decoded-transitions.bin" => "OPUSTRN1"u8,
                _ => throw new ArgumentOutOfRangeException(nameof(filename))
            };
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual(magic))
            {
                throw new InvalidDataException("The CELT PCM reference header is invalid.");
            }
            MediaBufferAllocator allocator = new(256 * 1024);
            using CeltTransformPlan plan = CeltTransformPlan.Create(allocator);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            Span<float> pcm = stackalloc float[1920];
            Span<Range> ranges = stackalloc Range[48];
            int groups = reader.ReadInt32(), frames = 0;
            long tables = allocator.AllocatedBytes;
            for (int group = 0; group < groups; group++)
            {
                int channels = reader.ReadInt32();
                bool disableInversion = reader.ReadInt32() != 0;
                int count = reader.ReadInt32();
                if (disableInversion != (channels == 1)) { throw new InvalidDataException("The Opus reference phase inversion differs from its channel mapping."); }
                using (OpusFrameDecoder decoder = new(plan, codebook, allocator, channels))
                {
                    for (int frame = 0; frame < count; frame++)
                    {
                        byte[] packet = reader.ReadBytes(reader.ReadInt32());
                        int expectedSamples = reader.ReadInt32();
                        uint expectedRange = reader.ReadUInt32();
                        OpusPacketInfo layout = OpusPacketInfo.ReadFrames(packet, ranges);
                        OpusPacketConfiguration configuration = OpusPacketConfiguration.FromToc(packet[0]);
                        int decoded = 0;
                        for (int at = 0; at < layout.FrameCount; at++)
                        {
                            decoded += decoder.Decode(packet.AsSpan()[ranges[at]], configuration, pcm[(decoded * channels)..]);
                        }
                        if (decoded != expectedSamples || decoder.FinalRange != expectedRange)
                        {
                            throw new InvalidDataException($"CELT stream {group}, frame {frame}, sample count/range differs: {decoded}, {decoder.FinalRange} / {expectedSamples}, {expectedRange}.");
                        }
                        Compare(reader, pcm[..(decoded * channels)], group, frame);
                        frames++;
                    }
                }
                if (allocator.AllocatedBytes != tables) { throw new InvalidDataException("The CELT decoder retained its working allocation."); }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length) { throw new InvalidDataException("The CELT PCM reference has trailing bytes."); }
            codebook.Dispose(); plan.Dispose();
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("The CELT PCM tables retained their allocation."); }
            return frames;
        }

        private static void Compare(BinaryReader reader, ReadOnlySpan<float> pcm, int group, int frame)
        {
            double signal = 0, error = 0;
            float peak = 0, peakError = 0;
            for (int at = 0; at < pcm.Length; at++)
            {
                float expected = reader.ReadSingle(), actual = pcm[at];
                if (!float.IsFinite(actual)) { throw new InvalidDataException($"CELT stream {group}, frame {frame}, sample {at}, is not finite."); }
                double difference = (double)actual - expected;
                signal += (double)expected * expected; error += difference * difference;
                peak = Math.Max(peak, Math.Abs(expected)); peakError = Math.Max(peakError, (float)Math.Abs(difference));
            }
            double relative = Math.Sqrt(error / Math.Max(1e-20, signal));
            if (relative > .00001 || peakError > .000003f * Math.Max(1, peak))
            {
                throw new InvalidDataException($"CELT stream {group}, frame {frame}, PCM relative error {relative}, peak error {peakError} / {peak}.");
            }
        }
    }
}
