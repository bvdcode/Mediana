// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual.Tests
{
    internal static class Mpeg4TestInput
    {
        internal static List<byte[]> Packets(byte[] data)
        {
            List<byte[]> packets = []; int start = 0;
            for (int at = Mpeg4VisualStartCodes.Find(data); at >= 0; at = Mpeg4VisualStartCodes.Find(data, at + 4))
            {
                if (data[at + 3] != 0xb6) { continue; }
                int next = Mpeg4VisualStartCodes.Find(data, at + 4), end = next < 0 ? data.Length : next;
                packets.Add(data.AsSpan(start, end - start).ToArray()); start = end;
            }
            return packets;
        }
        internal static MediaPacket Packet(MediaBufferAllocator allocator, ReadOnlySpan<byte> data, MediaTimestamp? time = null, MediaDuration? duration = null)
        {
            using WritableMediaBuffer storage = allocator.Allocate(Math.Max(1, data.Length)); data.CopyTo(storage.Span);
            using MediaBuffer complete = storage.Publish(); using MediaBuffer view = complete.Slice(0, data.Length);
            return new(2, view, time, duration: duration);
        }
        internal static EncodedStreamInfo Stream(byte[] data, bool outOfBand)
        {
            Mpeg4VisualConfiguration config = Mpeg4VisualConfiguration.Read(data);
            int at = Mpeg4VisualStartCodes.Find(data);
            while (data[at + 3] != 0xb6) { at = Mpeg4VisualStartCodes.Find(data, at + 4); }
            return new(2, MediaKind.Video, "mpeg4", StreamFormats.Mpeg4Visual, new(1, config.TimeIncrementResolution),
                outOfBand ? data.AsSpan(0, at) : default);
        }
        internal static void VerifyPixels(VideoFrame frame, ReadOnlySpan<byte> expected)
        {
            int at = 0, maximum = 0; long square = 0;
            for (int plane = 0; plane < frame.Planes.Count; plane++)
            {
                for (int row = 0; row < frame.Planes[plane].Height; row++)
                {
                    foreach (byte sample in frame.GetRow(plane, row))
                    { int error = Math.Abs(sample - expected[at++]); maximum = Math.Max(maximum, error); square += error * error; }
                }
            }
            double psnr = square == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255 * at / square);
            if (maximum > 6 || psnr < 48) { throw new InvalidDataException($"MPEG-4 Visual samples differ: PSNR {psnr:F3}, maximum error {maximum}."); }
        }
    }
}
