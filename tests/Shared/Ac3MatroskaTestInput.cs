// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs;
using Mediana.Codecs.Ac3;
using Mediana.Containers.Matroska;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Audio;
using static Mediana.Tests.MatroskaIndexTestData;
using static Mediana.Tests.MatroskaMetadataTestData;
using static Mediana.Tests.MatroskaReaderTestData;

namespace Mediana.Tests
{
    internal static class Ac3MatroskaTestInput
    {
        public static async Task VerifyAsync(int channels, bool laced)
        {
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Ac3", $"dependent-{channels}.eac3"));
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Ac3", $"dependent-{channels}.pcm"));
            List<byte[]> frames = Frames(encoded);
            byte[] data = Create(frames, channels, laced);
            const int maximumPayloadPrefixBytes = 256;
            List<(long Start, long End)> excluded = [];
            int search = 0;
            foreach (byte[] frame in frames)
            {
                int offset = search + data.AsSpan(search).IndexOf(frame);
                Require(offset >= search); search = offset + frame.Length; excluded.Add((offset + maximumPayloadPrefixBytes, search));
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using EbmlElementTestSource source = new(data) { MaximumRead = 3, ExcludedReadRanges = excluded };
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator))
            {
                Require(reader.Streams[0].Format == StreamFormats.Eac3Syncframe);
                Require(reader.GetStreamMetadata(0) is AudioStreamMetadata metadata && metadata.ChannelCount == channels);
                Require(reader.GetPacketCount(0) == frames.Count);
                int calls = source.ReadCalls;
                for (int index = frames.Count - 1; index >= 0; index--)
                {
                    Ac3FrameHeader header = Ac3FrameHeader.Read(frames[index]);
                    bool dependent = header.StreamType == 1;
                    Require(reader.GetPacketTiming(0, index).Duration == new MediaDuration(768, new(1, 48000)));
                    Require(reader.GetPacketTiming(0, index).IsKeyFrame == !dependent);
                    Require(reader.GetPresentationDuration(0, index) == new MediaDuration(dependent ? 0 : 768, new(1, 48000)));
                }
                Require(reader.FindSeekPoint(0, new(2000, new(1, 48000)), frames.Count)!.Value.PacketIndex == 2 * (channels == 8 ? 2 : 4));
                Require(source.ReadCalls == calls);
                source.ExcludedReadRanges = null;
                using IMediaCodec<MediaPacket, AudioFrame> decoder = new Eac3DecoderFactory(false).Create(reader.Streams[0], allocator);
                int samples = 0;
                for (int index = 0; index < frames.Count; index++)
                {
                    using MediaPacket packet = (await reader.ReadPacketAsync())!;
                    Require(packet.Data.Span.SequenceEqual(frames[index]));
                    Require(decoder.TrySend(packet) == CodecInputStatus.Accepted); Drain();
                }
                decoder.CompleteInput(); Drain();
                Require(samples == 3072 && decoder.Receive().Status == CodecOutputStatus.EndOfStream);

                void Drain()
                {
                    while (decoder.Receive() is { Status: CodecOutputStatus.Available } available)
                    {
                        using AudioFrame frame = available.Value;
                        Require(frame.Format.Channels.Count == channels && frame.SamplesPerChannel == 768);
                        Require(frame.Timestamp!.Value.CompareTo(new(samples, new(1, 48000))) == 0);
                        ReadOnlySpan<float> actual = MemoryMarshal.Cast<byte, float>(frame.GetPlane(0).Span);
                        ReadOnlySpan<float> expected = MemoryMarshal.Cast<byte, float>(reference.AsSpan(samples * channels * sizeof(float), frame.GetPlane(0).Length));
                        for (int index = 0; index < actual.Length; index++) { Require(Math.Abs(actual[index] - expected[index]) < 1e-8); }
                        samples += frame.SamplesPerChannel;
                    }
                }
            }
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory()).AddAudioDecoder(new Eac3DecoderFactory(false)).Build();
            source.ExcludedReadRanges = excluded;
            MediaInfo info = await engine.ProbeAsync(source);
            Require(info.StreamMetadata[0] is AudioStreamMetadata facts && facts.ChannelCount == channels && facts.ChannelOrder?.Count == channels);
            source.ExcludedReadRanges = null;
            await using (AudioSession session = await engine.OpenAudioAsync(source))
            {
                Require(session.CanSeek); await session.SeekAsync(new(2000, new(1, 48000)));
                using AudioFrame frame = (await session.ReadFrameAsync())!;
                Require(frame.Timestamp!.Value.CompareTo(new(1536, new(1, 48000))) == 0 && frame.SamplesPerChannel == 768);
                Require(frame.GetPlane(0).Span.SequenceEqual(reference.AsSpan(1536 * channels * sizeof(float), frame.GetPlane(0).Length)));
                frame.Dispose(); await session.DisposeAsync(); Require(session.AllocatedBufferBytes == 0);
            }
            Require(!source.IsDisposed);
        }

        internal static List<byte[]> Frames(byte[] encoded)
        {
            List<byte[]> frames = [];
            for (int offset = 0; offset < encoded.Length;)
            {
                Ac3FrameHeader header = Ac3FrameHeader.Read(encoded.AsSpan(offset));
                frames.Add(encoded.AsSpan(offset, header.FrameBytes).ToArray()); offset += header.FrameBytes;
            }
            return frames;
        }

        internal static byte[] Create(IReadOnlyList<byte[]> frames, int channels, bool laced, bool clipped = false)
        {
            byte[] audio = Field(0xE1, [.. Float(0xB5, 48000), .. Unsigned(0x9F, (ulong)channels)]);
            byte[] track = Field(0x1654AE6B, Field(0xAE, [.. Unsigned(0xD7, 1), .. Unsigned(0x73C5, 2), .. Unsigned(0x83, 2),
                .. Field(0x86, System.Text.Encoding.UTF8.GetBytes("A_EAC3")), .. audio]));
            List<byte> blocks = [];
            if (laced)
            {
                List<byte> payload = [(byte)(frames.Count - 1)];
                foreach (byte[] frame in frames.Take(frames.Count - 1))
                {
                    int length = frame.Length;
                    while (length >= 255) { payload.Add(255); length -= 255; }
                    payload.Add((byte)length);
                }
                foreach (byte[] frame in frames) { payload.AddRange(frame); }
                byte[] block = Block(clipped ? 0xA1u : 0xA3u, flags: clipped ? (byte)0x02 : (byte)0x82, payload: payload.ToArray());
                blocks.AddRange(clipped ? Field(0xA0, [.. block, .. Unsigned(0x9B, 60)]) : block);
            }
            else
            {
                short timestamp = -16;
                foreach (byte[] frame in frames)
                {
                    bool dependent = Ac3FrameHeader.Read(frame).StreamType == 1;
                    if (!dependent) { timestamp += 16; }
                    blocks.AddRange(Block(flags: dependent ? (byte)0 : (byte)0x80, timestamp: timestamp, payload: frame));
                }
            }
            return Document(Field(0x1549A966, InfoFields), track, Cluster(Unsigned(0xE7, 0), blocks.ToArray()));
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("The Matroska E-AC-3 programme, exact clock, bounded indexing or resource ownership is incorrect."); }
        }
    }
}
