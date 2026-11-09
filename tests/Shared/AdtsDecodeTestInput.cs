// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Containers;
using Mediana.Containers.Adts;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class AdtsDecodeTestInput
    {
        public static async Task VerifyProtectedAsync(string root, string name, int channels, bool indexed)
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, name + ".aac"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using (MemoryStream file = new(input, writable: false))
            await using (IByteSource source = indexed ? new RandomAccessStreamByteSource(file)
                : new StreamByteSource(file))
            await using (IContainerReader reader = await new AdtsReaderFactory().OpenAsync(source, allocator))
            {
                AacDecoderFactory factory = new();
                IPacketIntegrityValidator validator = factory.CreateIntegrityValidator(reader.Streams[0]);
                using IMediaCodec<MediaPacket, AudioFrame> decoder = factory.Create(reader.Streams[0], allocator);
                int frames = 0;
                while (await reader.ReadPacketAsync() is MediaPacket packet)
                {
                    using (packet)
                    {
                        if (packet.SideDataCount != 0 || validator.Validate(packet) != PacketIntegrityStatus.Verified)
                        {
                            throw new InvalidDataException("Protected ADTS transport packet lost its embedded integrity fields.");
                        }
                        using MediaPacket retained = packet.Retain();
                        packet.Dispose();
                        decoder.TrySend(retained);
                        using AudioFrame frame = decoder.Receive().Value;
                        if (frame.SamplesPerChannel != 1024 || frame.Format.Channels.Count != channels
                            || frame.Timestamp != new MediaTimestamp((long)frames * 1024, new TimeBase(1, 48000)))
                        {
                            throw new InvalidDataException("Protected ADTS decoding changed its sample count, channels or timeline.");
                        }
                        frames++;
                    }
                }
                if (frames != 6)
                {
                    throw new InvalidDataException("Protected ADTS decoding did not consume all six reference frames.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Protected ADTS decoding retained packet or sample storage.");
            }
        }

        public static async Task VerifyAsync(string root, string name, int rate, int channels, bool indexed)
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, name + ".aac"));
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + ".f32le"));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new AdtsReaderFactory())
                .AddAudioDecoder(new AacDecoderFactory()).Build();
            await using MemoryStream file = new(input, writable: false);
            await using IByteSource source = indexed ? new RandomAccessStreamByteSource(file, leaveOpen: true)
                : new StreamByteSource(file, leaveOpen: true);
            await using AudioSession session = await engine.OpenAudioAsync(source);
            List<byte[]> sequential = [];
            long total = 0;
            double maximumError = 0;
            TimeBase clock = new(1, rate);
            while (await session.ReadFrameAsync() is AudioFrame frame)
            {
                using (frame)
                {
                    if (frame.Format.SampleRate != rate || frame.Format.Channels.Count != channels
                        || frame.Timestamp != new MediaTimestamp(total, clock) || frame.SamplesPerChannel != 1024)
                    {
                        throw new InvalidDataException("ADTS decoding changed its sample format or timeline.");
                    }
                    ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(frame.GetPlane(0).Span);
                    long offset = checked(total * channels * sizeof(float));
                    if (offset + samples.Length * sizeof(float) > reference.Length)
                    {
                        throw new InvalidDataException("ADTS decoding exceeded the independent PCM reference.");
                    }
                    for (int index = 0; index < samples.Length; index++)
                    {
                        float expected = BinaryPrimitives.ReadSingleLittleEndian(reference.AsSpan(checked((int)offset + index * sizeof(float))));
                        if (!float.IsFinite(samples[index])) { throw new InvalidDataException("ADTS decoding produced nonfinite PCM."); }
                        maximumError = Math.Max(maximumError, Math.Abs(samples[index] - expected));
                    }
                    sequential.Add(frame.GetPlane(0).ToArray());
                    total += frame.SamplesPerChannel;
                }
            }
            if (total == 0 || reference.Length != total * channels * sizeof(float) || maximumError > 2e-6)
            {
                throw new InvalidDataException($"ADTS PCM reference mismatch; maximum error {maximumError:G9}.");
            }
            if (session.CanSeek != indexed) { throw new InvalidDataException("The ADTS reader's seeking capability changed."); }
            if (indexed)
            {
                foreach (int candidate in Enumerable.Range(0, sequential.Count).Reverse().Concat([0, sequential.Count - 1, 0]))
                {
                    await session.SeekAsync(new((long)candidate * 1024, clock));
                    for (int index = candidate; index < sequential.Count; index++)
                    {
                        using AudioFrame frame = await session.ReadFrameAsync() ?? throw new InvalidDataException("ADTS seeking lost a frame.");
                        if (frame.Timestamp != new MediaTimestamp((long)index * 1024, clock)
                            || !frame.GetPlane(0).Span.SequenceEqual(sequential[index]))
                        {
                            throw new InvalidDataException("ADTS seek PCM differs from continuous decoding.");
                        }
                    }
                    if (await session.ReadFrameAsync() is not null) { throw new InvalidDataException("ADTS seeking produced extra output."); }
                }
            }
            await session.DisposeAsync();
            if (session.AllocatedBufferBytes != 0 || !file.CanRead || (indexed && file.Position != 0))
            {
                throw new InvalidOperationException("ADTS decoding retained storage or changed borrowed source ownership.");
            }
        }
    }
}
