// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Codecs;
using Mediana.Codecs.Opus;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;

namespace Mediana.Tests
{
    internal static class OpusMultistreamTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "decoded-multistream.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSMST1"u8)) { throw new InvalidDataException("The Opus multistream reference header is invalid."); }
            MediaBufferAllocator allocator = new(1024 * 1024);
            int groups = reader.ReadInt32(), packets = 0;
            for (int group = 0; group < groups; group++)
            {
                int channels = reader.ReadInt32(), family = reader.ReadInt32(), streams = reader.ReadInt32(), coupled = reader.ReadInt32();
                byte[] mapping = reader.ReadBytes(channels);
                byte[] header = new byte[21 + channels]; "OpusHead"u8.CopyTo(header);
                header[8] = 1; header[9] = (byte)channels; header[18] = (byte)family;
                header[19] = (byte)streams; header[20] = (byte)coupled; mapping.CopyTo(header, 21);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 48000);
                EncodedStreamInfo stream = new(0, MediaKind.Audio, "opus", StreamFormats.Opus, new(1, 48000), header);
                using IMediaCodec<MediaPacket, AudioFrame> decoder = new OpusDecoderFactory().Create(stream, allocator);
                int frames = reader.ReadInt32(), clock = 0;
                for (int frame = 0; frame < frames; frame++)
                {
                    byte[] packet = reader.ReadBytes(reader.ReadInt32()); int samples = reader.ReadInt32();
                    using WritableMediaBuffer writable = allocator.Allocate(packet.Length); packet.CopyTo(writable.Span);
                    using MediaBuffer buffer = writable.Publish();
                    using MediaPacket input = new(0, buffer, new(clock, new(1, 48000)));
                    if (decoder.TrySend(input) != CodecInputStatus.Accepted) { throw new InvalidDataException("The Opus decoder did not accept the next reference packet."); }
                    using AudioFrame output = decoder.Receive().Value;
                    if (output.SamplesPerChannel != samples || output.Timestamp != new MediaTimestamp(clock, new(1, 48000)) || output.Format.Channels.Count != channels)
                    {
                        throw new InvalidDataException("The Opus multistream output shape or timestamp differs.");
                    }
                    Compare(reader, MemoryMarshal.Cast<byte, float>(output.GetPlane(0).Span), group, frame);
                    clock += samples; packets++;
                }
                decoder.CompleteInput();
                if (decoder.Receive().Status != CodecOutputStatus.EndOfStream) { throw new InvalidDataException("The Opus multistream decoder did not finish."); }
                decoder.Dispose();
                if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("The Opus multistream decoder retained storage."); }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length) { throw new InvalidDataException("The Opus multistream reference has trailing bytes."); }
            return packets;
        }

        private static void Compare(BinaryReader reader, ReadOnlySpan<float> pcm, int group, int frame)
        {
            double signal = 0, error = 0; float peakError = 0, peak = 0;
            for (int at = 0; at < pcm.Length; at++)
            {
                float expected = reader.ReadSingle(), actual = pcm[at];
                if (!float.IsFinite(actual)) { throw new InvalidDataException("The Opus multistream PCM is not finite."); }
                double difference = (double)actual - expected;
                signal += (double)expected * expected; error += difference * difference;
                peak = Math.Max(peak, Math.Abs(expected)); peakError = Math.Max(peakError, (float)Math.Abs(difference));
            }
            double relative = Math.Sqrt(error / Math.Max(1e-20, signal));
            if (relative > .00001 || peakError > .000003 * Math.Max(1, peak))
            {
                throw new InvalidDataException($"Opus multistream {group}, packet {frame}, PCM relative error {relative}, peak error {peakError}.");
            }
        }
    }
}
