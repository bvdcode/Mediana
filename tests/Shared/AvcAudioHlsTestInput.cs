// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    internal static class AvcAudioHlsTestInput
    {
        public static async Task VerifyAsync(string root, string name, string audioCodecs)
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name)));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator,
                new() { TimestampMode = Mp4TimestampMode.LinearMovie });
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            using VideoFrame first = await session.ReadFrameAsync() ?? throw new InvalidDataException("The A/V HLS source contains no video.");
            using H264Encoder encoder = H264Encoder.Create(first.Format, session.Stream.TimeBase, allocator);
            List<MemoryStream> outputs = [];
            await using AvcHlsWriter writer = AvcHlsWriter.Create(encoder.Stream, reader.Streams[1], (number, _) =>
            {
                if (number != outputs.Count)
                {
                    throw new InvalidDataException("The A/V HLS segment numbers are not contiguous.");
                }
                MemoryStream destination = new();
                outputs.Add(destination);
                return ValueTask.FromResult<Stream>(destination);
            }, allocator, new() { TargetDuration = new(1, new(1, 2)) });
            List<(int Stream, byte[] Data, long Pts, long Dts)> expected = [];
            int audio = 0;
            VideoFrame? frame = first.Retain();
            while (frame is not null)
            {
                using (frame)
                {
                    encoder.TrySend(frame);
                    using MediaPacket packet = encoder.Receive().Value ?? throw new InvalidDataException("A/V encoding produced no packet.");
                    while (audio < reader.Tracks[1].SampleCount && reader.GetSampleInfo(1, audio).DecodeTime.CompareTo(packet.DecodeTime!.Value) < 0)
                    {
                        using MediaPacket sound = await reader.ReadPacketAtAsync(1, audio++);
                        await WriteAsync(writer, sound, expected);
                    }
                    await WriteAsync(writer, packet, expected);
                }
                frame = await session.ReadFrameAsync();
            }
            while (audio < reader.Tracks[1].SampleCount)
            {
                using MediaPacket sound = await reader.ReadPacketAtAsync(1, audio++);
                await WriteAsync(writer, sound, expected);
            }
            await writer.CompleteAsync();
            byte[] transport = outputs.SelectMany(destination => destination.ToArray()).ToArray();
            MpegTsTestInput.Verify(transport, [encoder.Stream, reader.Streams[1]], expected);
            if (outputs.Count < 2 || writer.CreateVariant("av.m3u8").Codecs != "avc1.42C029," + audioCodecs ||
                writer.CreateVodPlaylist(number => $"{number}.ts").Contains("#EXT-X-INDEPENDENT-SEGMENTS", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The audiovisual HLS metadata or segment count is incorrect.");
            }
            if (writer.Segments[0].Start != first.Timestamp!.Value ||
                writer.Segments.Sum(segment => segment.ByteLength) != transport.LongLength)
            {
                throw new InvalidDataException("The HLS timeline start or byte accounting differs from the presentation.");
            }
            for (int index = 0; index + 1 < writer.Segments.Count; index++)
            {
                HlsMediaSegment segment = writer.Segments[index];
                MediaTimestamp end = new(checked(segment.Start.Value + segment.Duration.Value), segment.Duration.TimeBase);
                if (end != writer.Segments[index + 1].Start)
                {
                    throw new InvalidDataException("The A/V HLS segment timeline has a gap or overlap.");
                }
            }
            Mp4SampleInfo lastAudio = reader.GetSampleInfo(1, reader.Tracks[1].SampleCount - 1);
            MediaTimestamp audioEnd = new(checked(lastAudio.PresentationTime.Value + lastAudio.Duration.Value), lastAudio.Duration.TimeBase);
            HlsMediaSegment last = writer.Segments[^1];
            MediaTimestamp presentationEnd = new(checked(last.Start.Value + last.Duration.Value), last.Duration.TimeBase);
            if (presentationEnd.CompareTo(audioEnd) < 0)
            {
                throw new InvalidDataException("The HLS presentation omits the audio tail.");
            }
            await writer.DisposeAsync();
            encoder.Dispose();
            first.Dispose();
            await session.DisposeAsync();
            await reader.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("The A/V HLS pipeline retained media storage.");
            }
        }

        private static async ValueTask WriteAsync(AvcHlsWriter writer, MediaPacket packet,
            List<(int Stream, byte[] Data, long Pts, long Dts)> expected)
        {
            expected.Add((packet.StreamIndex, packet.Data.ToArray(), MpegTsTestInput.Ticks(packet.PresentationTime), MpegTsTestInput.Ticks(packet.DecodeTime)));
            await writer.WritePacketAsync(packet);
        }
    }
}
