// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    /// <summary>VP9 superframe member extents, obtained from at most 34 trailing bytes.</summary>
    public static class Vp9SuperframeIndex
    {
        public const int MaximumFrames = 8;
        public const int MaximumIndexBytes = 2 + MaximumFrames * sizeof(uint);

        public static int Read(ReadOnlySpan<byte> tail, int packetLength, Span<Range> frames)
        {
            if (packetLength <= 0 || tail.IsEmpty) { throw new InvalidDataException("A VP9 packet must contain a coded frame."); }
            if (frames.Length < MaximumFrames) { throw new ArgumentException("VP9 superframe indexing requires eight range slots.", nameof(frames)); }
            byte marker = tail[^1];
            if ((marker & 0xe0) != 0xc0) { frames[0] = 0..packetLength; return 1; }
            int count = (marker & 7) + 1, bytes = ((marker >> 3) & 3) + 1, indexLength = 2 + count * bytes;
            if (indexLength > packetLength) { frames[0] = 0..packetLength; return 1; }
            if (tail.Length < indexLength) { throw new ArgumentException("The VP9 packet tail does not contain its complete superframe index.", nameof(tail)); }
            ReadOnlySpan<byte> index = tail[^indexLength..];
            if (index[0] != marker) { frames[0] = 0..packetLength; return 1; }
            int payloadLength = packetLength - indexLength, cursor = 1, offset = 0;
            for (int i = 0; i < count; i++)
            {
                uint length = 0;
                for (int b = 0; b < bytes; b++) { length |= (uint)index[cursor++] << (b * 8); }
                if (length == 0 || length > payloadLength - offset) { throw new InvalidDataException("A VP9 superframe size exceeds its packet payload."); }
                int end = checked(offset + (int)length); frames[i] = offset..end; offset = end;
            }
            if (offset != payloadLength) { throw new InvalidDataException("VP9 superframe sizes do not cover the packet payload."); }
            return count;
        }
    }
}
