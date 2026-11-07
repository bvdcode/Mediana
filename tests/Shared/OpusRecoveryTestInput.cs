// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Runtime.InteropServices;
using Mediana.Codecs;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Codecs.Opus;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    internal static class OpusRecoveryTestInput
    {
        public static int Verify(string root, string filename)
        {
            using GZipStream stream = new(File.OpenRead(Path.Combine(root, filename)), CompressionMode.Decompress);
            using BinaryReader reader = new(stream);
            bool fixedPcm = filename == "decoded-silk-recovery.bin.gz";
            ReadOnlySpan<byte> magic = filename switch
            {
                "decoded-silk-recovery.bin.gz" => "OPUSREC1"u8,
                "decoded-celt-recovery.bin.gz" => "OPUSCRC1"u8,
                "decoded-hybrid-recovery.bin.gz" => "OPUSHRC1"u8,
                "decoded-unprimed-transitions.bin.gz" => "OPUSUTC1"u8,
                _ => throw new ArgumentOutOfRangeException(nameof(filename))
            };
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual(magic)) { throw new InvalidDataException("The Opus recovery reference header is invalid."); }
            MediaBufferAllocator allocator = new(512 * 1024);
            using CeltTransformPlan plan = CeltTransformPlan.Create(allocator);
            using CeltPulseCodebook codebook = CeltPulseCodebook.Create(176, 128, allocator);
            Span<float> output = stackalloc float[5760], expected = stackalloc float[5760];
            Span<Range> ranges = stackalloc Range[48];
            int groups = reader.ReadInt32(), frames = 0;
            long tables = allocator.AllocatedBytes;
            for (int group = 0; group < groups; group++)
            {
                _ = reader.ReadInt32();
                int channels = reader.ReadInt32(), count = reader.ReadInt32();
                double streamError = 0, streamEnergy = 0;
                byte[] header = new byte[19]; "OpusHead"u8.CopyTo(header); header[8] = 1; header[9] = (byte)channels;
                using IMediaCodec<MediaPacket, AudioFrame> publicDecoder = new OpusDecoderFactory().Create(
                    new(0, MediaKind.Audio, "opus", StreamFormats.Opus, new(1, 48000), header), allocator);
                byte previousToc = 0; long clock = 0;
                using (OpusFrameDecoder decoder = new(plan, codebook, allocator, channels, channels == 1))
                {
                    for (int frame = 0; frame < count; frame++)
                    {
                        byte[] packet = reader.ReadBytes(reader.ReadInt32());
                        int samples = reader.ReadInt32(); uint range = reader.ReadUInt32();
                        int decoded = 0;
                        if (packet.Length == 0) { decoded = decoder.Conceal(samples, output); }
                        else
                        {
                            OpusPacketInfo layout = OpusPacketInfo.ReadFrames(packet, ranges);
                            OpusPacketConfiguration coding = OpusPacketConfiguration.FromToc(packet[0]);
                            for (int at = 0; at < layout.FrameCount; at++)
                            {
                                decoded += decoder.Decode(packet.AsSpan()[ranges[at]], coding, output[(decoded * channels)..]);
                            }
                        }
                        if (decoder.FinalRange != range) { throw new InvalidDataException($"Opus recovery {group}/{frame} final range differs: {decoder.FinalRange} / {range}."); }
                        if (decoded != samples) { throw new InvalidDataException("Opus recovery changed the frame length."); }
                        byte[] publicPacket = packet;
                        if (packet.Length == 0) { publicPacket = [(byte)(previousToc & 252)]; }
                        else { previousToc = packet[0]; }
                        using WritableMediaBuffer writable = allocator.Allocate(publicPacket.Length); publicPacket.CopyTo(writable.Span);
                        using MediaBuffer buffer = writable.Publish();
                        using MediaPacket input = new(0, buffer, new(clock, new(1, 48000)), duration: new(samples, new(1, 48000)));
                        if (publicDecoder.TrySend(input) != CodecInputStatus.Accepted) { throw new InvalidDataException("The public Opus decoder did not accept a recovery packet."); }
                        using AudioFrame publicFrame = publicDecoder.Receive().Value;
                        if (publicFrame.SamplesPerChannel != samples || publicFrame.Timestamp != new MediaTimestamp(clock, new(1, 48000)) ||
                            !MemoryMarshal.Cast<byte, float>(publicFrame.GetPlane(0).Span).SequenceEqual(output[..(samples * channels)]))
                        {
                            throw new InvalidDataException($"Public Opus recovery {group}/{frame} differs from its frame reconstruction.");
                        }
                        clock += samples;
                        double error = 0, energy = 0; float peakError = 0, peak = 0;
                        for (int at = 0; at < samples * channels; at++)
                        {
                            expected[at] = fixedPcm ? reader.ReadInt16() * (1f / 32768) : reader.ReadSingle();
                            float difference = output[at] - expected[at];
                            if (fixedPcm && difference != 0) { throw new InvalidDataException($"Opus SILK recovery {group}/{frame}/{at} differs: {output[at]} / {expected[at]}."); }
                            if (!float.IsFinite(output[at])) { throw new InvalidDataException("Opus recovery produced a nonfinite sample."); }
                            error += (double)difference * difference; energy += (double)expected[at] * expected[at];
                            peakError = Math.Max(peakError, Math.Abs(difference)); peak = Math.Max(peak, Math.Abs(expected[at]));
                        }
                        if (peakError > .02f * Math.Max(1, peak))
                        {
                            throw new InvalidDataException($"Opus recovery {group}/{frame} PCM differs: relative {Math.Sqrt(error / Math.Max(1e-30, energy))}, peak {peakError}.");
                        }
                        streamError += error; streamEnergy += energy;
                        frames++;
                    }
                }
                if (streamError > 1e-4 * Math.Max(1e-20, streamEnergy))
                {
                    throw new InvalidDataException($"Opus recovery stream {group} has less than 40 dB signal-to-error ratio.");
                }
                publicDecoder.CompleteInput(); publicDecoder.Dispose();
                if (allocator.AllocatedBytes != tables) { throw new InvalidDataException("The Opus recovery decoder retained its working allocation."); }
            }
            if (stream.ReadByte() != -1) { throw new InvalidDataException("The Opus recovery reference has trailing bytes."); }
            return frames;
        }
    }
}
