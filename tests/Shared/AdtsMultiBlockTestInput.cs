// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Containers;
using Mediana.Containers.Adts;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Audio;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class AdtsMultiBlockTestInput
    {
        public static async Task VerifyEnhancementAsync(string root, string name, int channels)
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, name + ".aac"));
            byte[][] expected = await ReadEnhancedPcmAsync(input, channels);
            using MemoryStream regrouped = new();
            List<(int Offset, AdtsFrameHeader Header)> frames = [];
            for (int offset = 0; offset < input.Length;)
            {
                AdtsFrameHeader header = AdtsFrameHeader.Read(input.AsSpan(offset));
                if (header.HasCrc || header.RawDataBlockCount != 1)
                {
                    throw new InvalidDataException("The enhancement baseline requires single-block unprotected transport frames.");
                }
                frames.Add((offset, header));
                offset += header.FrameBytes;
            }
            int next = 0;
            int group = 0;
            while (next < frames.Count)
            {
                int count = Math.Min(group++ % 4 + 1, frames.Count - next);
                int bytes = 7;
                for (int index = 0; index < count; index++)
                {
                    bytes += frames[next + index].Header.FrameBytes - 7;
                }
                byte[] header = input.AsSpan(frames[next].Offset, 7).ToArray();
                header[3] = (byte)((header[3] & 0xfc) | bytes >> 11);
                header[4] = (byte)(bytes >> 3);
                header[5] = (byte)((bytes & 7) << 5 | 31);
                header[6] = (byte)(0xfc | count - 1);
                regrouped.Write(header);
                for (int index = 0; index < count; index++)
                {
                    (int offset, AdtsFrameHeader frame) = frames[next + index];
                    regrouped.Write(input.AsSpan(offset + 7, frame.FrameBytes - 7));
                }
                next += count;
            }
            byte[][] actual = await ReadEnhancedPcmAsync(regrouped.ToArray(), channels);
            if (actual.Length != expected.Length)
            {
                throw new InvalidDataException("Regrouping enhanced AAC changed its raw block count.");
            }
            for (int index = 0; index < actual.Length; index++)
            {
                if (!actual[index].AsSpan().SequenceEqual(expected[index]))
                {
                    throw new InvalidDataException("Regrouping ADTS changed SBR or parametric stereo reconstruction.");
                }
            }
        }

        private static async Task<byte[][]> ReadEnhancedPcmAsync(byte[] input, int channels)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new AdtsReaderFactory())
                .AddAudioDecoder(new AacDecoderFactory()).Build();
            await using MemoryStream file = new(input, writable: false);
            await using StreamByteSource source = new(file);
            await using AudioSession session = await engine.OpenAudioAsync(source);
            List<byte[]> frames = [];
            while (await session.ReadFrameAsync() is AudioFrame frame)
            {
                using (frame)
                {
                    if (frame.Format.SampleRate != 48000 || frame.Format.Channels.Count != channels || frame.SamplesPerChannel != 2048
                        || frame.Timestamp != new MediaTimestamp((long)frames.Count * 1024, new(1, 24000)))
                    {
                        throw new InvalidDataException("Enhanced ADTS lost its reconstructed format or core sample grid.");
                    }
                    frames.Add(frame.GetPlane(0).ToArray());
                }
            }
            await session.DisposeAsync();
            if (session.AllocatedBufferBytes != 0 || frames.Count == 0)
            {
                throw new InvalidDataException("Enhanced ADTS is empty or retained its buffers.");
            }
            return frames.ToArray();
        }

        public static async Task VerifyAsync(string root, bool protection, bool indexed)
        {
            byte[][] expected = await ReadBaselineAsync(root);
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, protection ? "multi-protected.aac" : "multi-unprotected.aac"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using (MemoryStream file = new(input, writable: false))
            await using (IByteSource source = indexed ? new RandomAccessStreamByteSource(file, leaveOpen: true)
                : new StreamByteSource(file, leaveOpen: true))
            await using (IContainerReader reader = await new AdtsReaderFactory().OpenAsync(source, allocator))
            {
                EncodedStreamInfo stream = reader.Streams[0];
                if (stream.Format != StreamFormats.AacAdts)
                {
                    throw new InvalidDataException("ADTS transport framing was not preserved.");
                }
                AacDecoderFactory factory = new();
                IPacketIntegrityValidator integrity = factory.CreateIntegrityValidator(stream);
                IRandomAccessInspector inspector = factory.CreateRandomAccessInspector(stream);
                using IMediaCodec<MediaPacket, AudioFrame> decoder = factory.Create(stream, allocator);
                int offset = 0;
                int outputCount = 0;
                int packetCount = 0;
                while (await reader.ReadPacketAsync() is MediaPacket packet)
                {
                    using (packet)
                    {
                        AdtsFrameHeader header = AdtsFrameHeader.Read(input.AsSpan(offset));
                        if (packet.SideDataCount != 0 || packet.Data.Length != header.FrameBytes
                            || !packet.Data.Span.SequenceEqual(input.AsSpan(offset, header.FrameBytes))
                            || packet.PresentationTime != new MediaTimestamp((long)outputCount * 1024, new(1, 48000))
                            || packet.Duration != new MediaDuration(header.RawDataBlockCount * 1024, new(1, 48000)))
                        {
                            throw new InvalidDataException("ADTS transport decoding changed packet bytes or its multi-block timeline.");
                        }
                        PacketIntegrityStatus status = integrity.Validate(packet);
                        if (status != (protection ? PacketIntegrityStatus.Verified : PacketIntegrityStatus.NotPresent)
                            || !inspector.IsRandomAccess(packet))
                        {
                            throw new InvalidDataException("The complete LC ADTS frame did not validate as an independent packet.");
                        }
                        if (decoder.TrySend(packet) != CodecInputStatus.Accepted || decoder.TrySend(packet) != CodecInputStatus.OutputRequired)
                        {
                            throw new InvalidOperationException("The ADTS decoder did not preserve output backpressure.");
                        }
                        for (int block = 0; block < header.RawDataBlockCount; block++)
                        {
                            using AudioFrame frame = decoder.Receive().Value;
                            RequireFrame(frame, expected, outputCount++);
                        }
                        if (decoder.Receive().Status != CodecOutputStatus.InputRequired)
                        {
                            throw new InvalidDataException("The ADTS frame produced an incorrect number of raw blocks.");
                        }
                        offset += header.FrameBytes;
                        packetCount++;
                    }
                }
                decoder.CompleteInput();
                if (decoder.Receive().Status != CodecOutputStatus.EndOfStream || outputCount != 18 || packetCount != 11
                    || offset != input.Length || indexed && stream.CodedDuration != new MediaDuration(18 * 1024, new(1, 48000)))
                {
                    throw new InvalidDataException("The mixed-block ADTS sequence did not reach its exact end.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Multi-block ADTS decoding retained sample or packet storage.");
            }
            if (indexed)
            {
                await VerifySeeksAsync(input, expected);
            }
        }

        private static async Task VerifySeeksAsync(byte[] input, byte[][] expected)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new AdtsReaderFactory())
                .AddAudioDecoder(new AacDecoderFactory()).Build();
            await using MemoryStream file = new(input, writable: false);
            await using RandomAccessStreamByteSource source = new(file, leaveOpen: true);
            await using AdtsIndexedReader indexReader = await AdtsIndexedReader.OpenAsync(source, new(8192));
            await using AudioSession session = await engine.OpenAudioAsync(source);
            foreach (int candidate in new[] { 8, 1, 17, 4, 0, 8 })
            {
                MediaTimestamp target = new(candidate * 1024, new(1, 48000));
                ContainerSeekPoint point = indexReader.FindSeekPoint(0, target, indexReader.GetPacketCount(0))
                    ?? throw new InvalidDataException("The ADTS index has no output candidate.");
                int first = checked((int)(point.PresentationTime.Value / 1024));
                await session.SeekAsync(target);
                for (int index = first; index < expected.Length; index++)
                {
                    using AudioFrame frame = await session.ReadFrameAsync()
                        ?? throw new InvalidDataException("Multi-block ADTS seeking lost a frame.");
                    RequireFrame(frame, expected, index);
                }
                if (await session.ReadFrameAsync() is not null)
                {
                    throw new InvalidDataException("Multi-block ADTS seeking produced extra output.");
                }
            }
            await session.DisposeAsync();
            if (session.AllocatedBufferBytes != 0 || file.Position != 0 || !file.CanRead)
            {
                throw new InvalidOperationException("Multi-block ADTS seeking changed borrowed ownership or retained storage.");
            }
        }

        private static void RequireFrame(AudioFrame frame, byte[][] expected, int index)
        {
            if (index >= expected.Length || frame.SamplesPerChannel != 1024 || frame.Format.SampleRate != 48000
                || frame.Format.Channels.Count != 2 || frame.Timestamp != new MediaTimestamp((long)index * 1024, new(1, 48000))
                || !frame.GetPlane(0).Span.SequenceEqual(expected[index]))
            {
                throw new InvalidDataException($"ADTS raw block {index} differs from the single-block PCM sequence; timestamp {frame.Timestamp?.Value}, samples {frame.SamplesPerChannel}, rate {frame.Format.SampleRate}.");
            }
        }

        private static async Task<byte[][]> ReadBaselineAsync(string root)
        {
            byte[] sourceBytes = await File.ReadAllBytesAsync(Path.Combine(root, "tone-2.aac"));
            byte[] input = [.. sourceBytes, .. sourceBytes, .. sourceBytes];
            MediaBufferAllocator allocator = new(1024 * 1024);
            List<byte[]> frames = [];
            await using (MemoryStream file = new(input, writable: false))
            await using (StreamByteSource source = new(file))
            await using (AdtsReader reader = await AdtsReader.OpenAsync(source, allocator,
                new AdtsReaderOptions { PacketFraming = AdtsPacketFraming.RawAccessUnit }))
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(reader.Streams[0], allocator))
            {
                while (await reader.ReadPacketAsync() is MediaPacket packet)
                {
                    using (packet)
                    {
                        decoder.TrySend(packet);
                        using AudioFrame frame = decoder.Receive().Value;
                        frames.Add(frame.GetPlane(0).ToArray());
                    }
                }
            }
            if (allocator.AllocatedBytes != 0 || frames.Count != 18)
            {
                throw new InvalidDataException("The independent single-block transport baseline is incomplete.");
            }
            return frames.ToArray();
        }
    }
}
