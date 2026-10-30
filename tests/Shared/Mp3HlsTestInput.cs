// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    internal static class Mp3HlsTestInput
    {
        public static async Task VerifyAsync(string root, string name)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name)));
            IReadOnlyList<EncodedStreamInfo> streams;
            byte[][] outputs;
            await using (Mp4HlsSession session = await Mp4HlsSession.OpenAsync(source, allocator, 0, 1))
            {
                streams = session.Streams;
                outputs = new byte[session.Segments.Count][];
                if (outputs.Length < 2 || streams[1].CodecId != "mp3")
                {
                    throw new InvalidDataException("The MP3 HLS fixture did not produce multiple audiovisual segments.");
                }
                foreach (int number in Enumerable.Range(0, outputs.Length).Reverse().Concat(Enumerable.Range(0, outputs.Length)))
                {
                    using MemoryStream destination = new();
                    await session.WriteSegmentAsync(number, destination);
                    byte[] bytes = destination.ToArray();
                    if (outputs[number] is not null && !outputs[number].AsSpan().SequenceEqual(bytes))
                    {
                        throw new InvalidDataException("Repeated MP3 HLS segments differ.");
                    }
                    outputs[number] = bytes;
                }
                if (session.CreateVodPlaylist(number => $"{number}.ts").Contains("#EXT-X-INDEPENDENT-SEGMENTS", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("MP3 HLS must not declare independent audio decoding.");
                }
            }
            List<(int Stream, byte[] Data, long Pts, long Dts)> expected = [];
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                while (await reader.ReadPacketAsync() is MediaPacket packet)
                {
                    using (packet)
                    {
                        expected.Add((packet.StreamIndex, packet.Data.ToArray(), MpegTsTestInput.Ticks(packet.PresentationTime), MpegTsTestInput.Ticks(packet.DecodeTime)));
                    }
                }
            }
            MpegTsTestInput.Verify(outputs.SelectMany(bytes => bytes).ToArray(), streams, expected);
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("MP3 HLS retained media storage.");
            }
        }
    }
}
