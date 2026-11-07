// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Runtime.InteropServices;
using Mediana.Codecs;
using Mediana.Codecs.Vorbis;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    internal static class VorbisPcmTestInput
    {
        public static int Verify(string root, bool measureAllocations = false)
        {
            using FileStream file = File.OpenRead(Path.Combine(root, "spectrum.bin.gz"));
            using GZipStream gzip = new(file, CompressionMode.Decompress);
            using BinaryReader input = new(gzip);
            if (!input.ReadBytes(8).AsSpan().SequenceEqual("VORSPEC1"u8)) { throw new InvalidDataException("The Vorbis PCM reference header is invalid."); }
            int streams = input.ReadInt32(), total = 0;
            for (int stream = 0; stream < streams; stream++)
            {
                byte[] identificationBytes = input.ReadBytes(input.ReadInt32());
                VorbisIdentification identification = VorbisIdentification.Read(identificationBytes);
                byte[] setup = input.ReadBytes(input.ReadInt32());
                int frames = input.ReadInt32(), channels = identification.ChannelCount;
                MediaBufferAllocator allocator = new(32 * 1024 * 1024);
                using (VorbisFrameDecoder decoder = new(identification, setup, allocator))
                using (IMediaCodec<MediaPacket, AudioFrame> publicDecoder = CreatePublic(identificationBytes, setup, allocator))
                {
                    float[] output = new float[decoder.MaximumFrameSamples * channels];
                    TimeBase clock = new(1, identification.SampleRate);
                    long position = 0;
                    for (int frame = 0; frame < frames; frame++)
                    {
                        byte[] packet = input.ReadBytes(input.ReadInt32());
                        input.ReadInt64(); bool last = input.ReadInt32() != 0;
                        for (int channel = 0; channel < channels; channel++)
                        {
                            int bins = input.ReadInt32();
                            for (int index = 0; index < bins; index++) { input.ReadSingle(); }
                        }
                        int declared = decoder.GetOutputSamples(packet);
                        long reserved = allocator.AllocatedBytes;
                        long before = GC.GetAllocatedBytesForCurrentThread();
                        int samples = decoder.Decode(packet, output);
                        if (measureAllocations && (allocator.AllocatedBytes != reserved || GC.GetAllocatedBytesForCurrentThread() != before))
                        {
                            throw new InvalidDataException("The Vorbis frame decoder allocated working storage.");
                        }
                        int expected = input.ReadInt32();
                        if (declared != samples || expected > samples || (!last && expected != samples))
                        {
                            throw new InvalidDataException($"Vorbis PCM {stream}/{frame}: count {samples}/{declared}/{expected}.");
                        }
                        using WritableMediaBuffer writable = allocator.Allocate(packet.Length); packet.CopyTo(writable.Span);
                        using MediaBuffer buffer = writable.Publish();
                        using MediaPacket encoded = new(0, buffer, new(position, clock), duration: new(expected, clock));
                        if (publicDecoder.TrySend(encoded) != CodecInputStatus.Accepted)
                        {
                            throw new InvalidDataException("The public Vorbis decoder did not accept a reference packet.");
                        }
                        if (expected > 0 && publicDecoder.TrySend(encoded) != CodecInputStatus.OutputRequired)
                        {
                            throw new InvalidDataException("The public Vorbis decoder did not preserve pending PCM.");
                        }
                        CodecOutput<AudioFrame> received = publicDecoder.Receive();
                        using AudioFrame? published = received.Status == CodecOutputStatus.Available ? received.Value : null;
                        if (expected == 0)
                        {
                            if (received.Status != CodecOutputStatus.InputRequired) { throw new InvalidDataException("Vorbis priming published unexpected PCM."); }
                        }
                        else if (published is null || published.SamplesPerChannel != expected ||
                            published.Format.Channels.Count != channels || published.Format.SampleRate != identification.SampleRate ||
                            published.Timestamp != new MediaTimestamp(position, clock))
                        {
                            throw new InvalidDataException("The public Vorbis PCM shape or timestamp differs.");
                        }
                        ReadOnlySpan<float> publicPcm = published is null ? [] : MemoryMarshal.Cast<byte, float>(published.GetPlane(0).Span);
                        double error = 0, energy = 0, difference = 0, peak = 0;
                        for (int channel = 0; channel < channels; channel++)
                        {
                            for (int sample = 0; sample < expected; sample++)
                            {
                                float reference = input.ReadSingle(), actual = output[sample * channels + channel];
                                if (publicPcm[sample * channels + channel] != actual)
                                {
                                    throw new InvalidDataException("The public Vorbis decoder changed reconstructed PCM.");
                                }
                                double delta = actual - reference;
                                energy += (double)reference * reference; error += delta * delta;
                                peak = Math.Max(peak, Math.Abs(reference)); difference = Math.Max(difference, Math.Abs(delta));
                            }
                        }
                        if (error > 1e-10 * Math.Max(energy, 1e-20) || difference > 3e-6 * Math.Max(1, peak))
                        {
                            throw new InvalidDataException($"Vorbis PCM {stream}/{frame}: relative error {Math.Sqrt(error / Math.Max(energy, 1e-20)):R}; peak {difference:R}/{peak:R}.");
                        }
                        position += expected;
                        total++;
                    }
                    publicDecoder.CompleteInput();
                    if (publicDecoder.Receive().Status != CodecOutputStatus.EndOfStream) { throw new InvalidDataException("The public Vorbis decoder did not finish."); }
                }
                if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("Vorbis PCM storage was not released."); }
            }
            if (input.BaseStream.ReadByte() != -1) { throw new InvalidDataException("The Vorbis PCM reference has trailing data."); }
            return total;
        }

        private static IMediaCodec<MediaPacket, AudioFrame> CreatePublic(byte[] identification, byte[] setup, MediaBufferAllocator allocator)
        {
            byte[] comments = [3, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s', 0, 0, 0, 0, 0, 0, 0, 0, 1];
            byte[] configuration = new byte[3 + identification.Length + comments.Length + setup.Length];
            configuration[0] = 2; configuration[1] = (byte)identification.Length; configuration[2] = (byte)comments.Length;
            identification.CopyTo(configuration, 3); comments.CopyTo(configuration, 3 + identification.Length);
            setup.CopyTo(configuration, 3 + identification.Length + comments.Length);
            VorbisIdentification info = VorbisIdentification.Read(identification);
            EncodedStreamInfo stream = new(0, MediaKind.Audio, "vorbis", StreamFormats.Vorbis, new(1, info.SampleRate), configuration);
            return new VorbisDecoderFactory().Create(stream, allocator);
        }
    }
}
