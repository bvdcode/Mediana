// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264Nal
    {
        public static int Type(ReadOnlySpan<byte> nal)
        {
            if (nal.IsEmpty || (nal[0] & 0x80) != 0 || (nal[0] & 31) == 0)
            {
                throw new InvalidDataException("The H.264 NAL header is invalid.");
            }
            return nal[0] & 31;
        }

        public static H264BitReader Payload(ReadOnlySpan<byte> nal, int type)
        {
            if (Type(nal) != type)
            {
                throw new InvalidDataException($"Expected an H.264 NAL of type {type}.");
            }
            if (type is 5 or 7 or 8 && (nal[0] & 0x60) == 0)
            {
                throw new InvalidDataException("The H.264 reference NAL indicator is zero.");
            }
            return new H264BitReader(nal[1..]);
        }
    }
}
