// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats.Video;
using Mediana.IO;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    internal static class AvcColorRemuxTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            foreach (string name in new[] { "container-709-limited.mp4", "container-709-full.mp4", "container-709.mov",
                "container-601-conflict.mp4", "container-2020-pq.mp4", "container-segments.mp4" })
            {
                int sequenceCount = 0;
                await MpegTsTestInput.VerifyAsync(root, name, inspect: (stream, payload) =>
                    sequenceCount += VerifyColors(stream, payload.Span));
                if (sequenceCount == 0)
                {
                    throw new InvalidDataException("AVC remux did not emit source color parameters.");
                }
            }
            await VerifyHlsAsync(root);
        }

        private static async Task VerifyHlsAsync(string root)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, "container-segments.mp4")));
            byte[][] segments = new byte[3][];
            await using (Mp4HlsSession session = await Mp4HlsSession.OpenAsync(source, allocator, 0, 1, HlsSessionTestInput.Options()))
            {
                if (session.Segments.Count != 3 || session.Streams[0].ColorDescription is not CicpColorDescription)
                {
                    throw new InvalidDataException("HLS did not retain the source color description and segment windows.");
                }
                foreach (int number in new[] { 2, 0, 1, 2 })
                {
                    using MemoryStream output = new();
                    await session.WriteSegmentAsync(number, output);
                    byte[] bytes = output.ToArray();
                    if (segments[number] is not null && !segments[number].AsSpan().SequenceEqual(bytes))
                    {
                        throw new InvalidDataException("HLS color parameters changed between repeated segment requests.");
                    }
                    segments[number] = bytes;
                }
            }
            List<(int Stream, byte[] Data, long Pts, long Dts)> packets = [];
            int sequenceCount = 0;
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                while (await reader.ReadPacketAsync() is MediaPacket packet)
                {
                    using (packet)
                    {
                        packets.Add((packet.StreamIndex, packet.Data.ToArray(), MpegTsTestInput.Ticks(packet.PresentationTime),
                            MpegTsTestInput.Ticks(packet.DecodeTime)));
                    }
                }
                MpegTsTestInput.Verify(segments.SelectMany(segment => segment).ToArray(), reader.Streams, packets,
                    (stream, payload) => sequenceCount += VerifyColors(stream, payload.Span));
            }
            if (sequenceCount != 16 || allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("HLS lost rewritten configuration/in-band SPS parameters or retained media buffers.");
            }
        }

        private static int VerifyColors(EncodedStreamInfo stream, ReadOnlySpan<byte> payload)
        {
            if (stream.Kind != MediaKind.Video)
            {
                return 0;
            }
            CicpColorDescription color = stream.ColorDescription as CicpColorDescription
                ?? throw new InvalidDataException("The remux reference has no container color description.");
            int count = 0;
            while (!payload.IsEmpty)
            {
                if (!payload[..4].SequenceEqual(new byte[] { 0, 0, 0, 1 }))
                {
                    throw new InvalidDataException("The AVC output contains an invalid start code.");
                }
                payload = payload[4..];
                int next = payload.IndexOf(new byte[] { 0, 0, 0, 1 });
                int length = next < 0 ? payload.Length : next;
                if ((payload[0] & 31) == 7)
                {
                    H264VuiParameters vui = H264SequenceParser.Parse(payload[..length]).Vui
                        ?? throw new InvalidDataException("The AVC SPS has no source colors.");
                    if (vui.ColorPrimaries != color.Primaries || vui.TransferCharacteristics != color.Transfer ||
                        vui.MatrixCoefficients != color.Matrix || vui.FullRange != (color.FullRange ?? false))
                    {
                        throw new InvalidDataException("The AVC SPS differs from the declared container color description.");
                    }
                    count++;
                }
                payload = payload[length..];
            }
            return count;
        }
    }
}
