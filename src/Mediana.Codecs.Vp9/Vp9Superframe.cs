// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal static class Vp9Superframe
    {
        internal static int Parse(ReadOnlySpan<byte> packet, Span<Range> frames)
        {
            if (packet.IsEmpty) { throw new InvalidDataException("A VP9 packet must contain a coded frame."); }
            if (frames.Length < 8) { throw new ArgumentException("VP9 superframe indexing requires eight range slots.", nameof(frames)); }
            byte marker = packet[^1];
            if ((marker & 0xe0) != 0xc0) { frames[0] = 0..packet.Length; return 1; }
            int count = (marker & 7) + 1, bytes = ((marker >> 3) & 3) + 1, indexLength = 2 + count * bytes;
            if (indexLength > packet.Length || packet[packet.Length - indexLength] != marker)
            { frames[0] = 0..packet.Length; return 1; }
            int payloadLength = packet.Length - indexLength, cursor = payloadLength + 1, offset = 0;
            for (int i = 0; i < count; i++)
            {
                uint length = 0;
                for (int b = 0; b < bytes; b++) { length |= (uint)packet[cursor++] << (b * 8); }
                if (length == 0 || length > payloadLength - offset) { throw new InvalidDataException("A VP9 superframe size exceeds its packet payload."); }
                int end = checked(offset + (int)length); frames[i] = offset..end; offset = end;
            }
            if (offset != payloadLength) { throw new InvalidDataException("VP9 superframe sizes do not cover the packet payload."); }
            return count;
        }
    }
}
