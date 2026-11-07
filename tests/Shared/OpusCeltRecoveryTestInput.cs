// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Codecs.Opus;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    internal static class OpusCeltRecoveryTestInput
    {
        public static int Verify(string root)
        {
            using GZipStream stream = new(File.OpenRead(Path.Combine(root, "decoded-celt-recovery.bin.gz")), CompressionMode.Decompress);
            using BinaryReader reader = new(stream);
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSCRC1"u8)) { throw new InvalidDataException("The CELT recovery reference header is invalid."); }
            MediaBufferAllocator allocator = new(256 * 1024);
            using CeltTransformPlan plan = CeltTransformPlan.Create(allocator);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            Span<float> output = stackalloc float[1920], expected = stackalloc float[1920];
            Span<Range> ranges = stackalloc Range[48];
            int groups = reader.ReadInt32(), frames = 0;
            long tables = allocator.AllocatedBytes;
            for (int group = 0; group < groups; group++)
            {
                int bandwidth = reader.ReadInt32(), channels = reader.ReadInt32(), count = reader.ReadInt32();
                int endBand = bandwidth switch { 1101 => 13, 1103 => 17, 1104 => 19, 1105 => 21, _ => throw new InvalidDataException() };
                double streamError = 0, streamEnergy = 0;
                using (CeltFrameDecoder decoder = new(plan, codebook, allocator, channels, channels == 1))
                {
                    for (int frame = 0; frame < count; frame++)
                    {
                        byte[] packet = reader.ReadBytes(reader.ReadInt32());
                        int samples = reader.ReadInt32(); uint range = reader.ReadUInt32();
                        int shift = samples switch { 120 => 0, 240 => 1, 480 => 2, 960 => 3, _ => throw new InvalidDataException() };
                        int decoded;
                        if (packet.Length <= 2) { decoded = decoder.Conceal(new(channels, shift, 0, endBand), output); }
                        else
                        {
                            OpusPacketInfo layout = OpusPacketInfo.ReadFrames(packet, ranges);
                            if (layout.FrameCount != 1) { throw new InvalidDataException("The recovery reference contains more than one frame."); }
                            decoded = decoder.Decode(packet.AsSpan()[ranges[0]], CeltBandMode.FromConfiguration(OpusPacketConfiguration.FromToc(packet[0])), output);
                            if (decoder.FinalRange != range) { throw new InvalidDataException($"CELT recovery {group}/{frame} final range differs."); }
                        }
                        if (decoded != samples) { throw new InvalidDataException("CELT recovery changed the frame length."); }
                        double error = 0, energy = 0; float peakError = 0, peak = 0;
                        for (int at = 0; at < samples * channels; at++)
                        {
                            expected[at] = reader.ReadSingle();
                            float difference = output[at] - expected[at];
                            if (!float.IsFinite(output[at])) { throw new InvalidDataException("CELT recovery produced a nonfinite sample."); }
                            error += (double)difference * difference; energy += (double)expected[at] * expected[at];
                            peakError = Math.Max(peakError, Math.Abs(difference)); peak = Math.Max(peak, Math.Abs(expected[at]));
                        }
                        if (peakError > .02f * Math.Max(1, peak))
                        {
                            throw new InvalidDataException($"CELT recovery {group}/{frame} PCM differs: relative {Math.Sqrt(error / Math.Max(1e-30, energy))}, peak {peakError}.");
                        }
                        streamError += error; streamEnergy += energy;
                        frames++;
                    }
                }
                if (streamError > 1e-4 * Math.Max(1e-20, streamEnergy))
                {
                    throw new InvalidDataException($"CELT recovery stream {group} has less than 40 dB signal-to-error ratio.");
                }
                if (allocator.AllocatedBytes != tables) { throw new InvalidDataException("The CELT recovery decoder retained its working allocation."); }
            }
            if (stream.ReadByte() != -1) { throw new InvalidDataException("The CELT recovery reference has trailing bytes."); }
            return frames;
        }
    }
}
