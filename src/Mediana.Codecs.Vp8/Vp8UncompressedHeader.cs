// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp8
{
    internal readonly record struct Vp8UncompressedHeader(bool KeyFrame, bool ShowFrame, int Version,
        int ControlOffset, int ControlLength, int Width, int Height, int HorizontalScale, int VerticalScale)
    {
        internal static Vp8UncompressedHeader Parse(ReadOnlySpan<byte> encoded)
        {
            if (encoded.Length < 3) { throw new InvalidDataException("Truncated VP8 frame tag."); }
            int tag = encoded[0] | encoded[1] << 8 | encoded[2] << 16;
            bool key = (tag & 1) == 0;
            int version = (tag >> 1) & 7, offset = key ? 10 : 3, length = tag >> 5;
            if (version > 3) { throw new NotSupportedException("The VP8 frame version is reserved."); }
            if (encoded.Length < offset || length < 2 || length > encoded.Length - offset)
            { throw new InvalidDataException("Invalid VP8 control partition extent."); }
            int width = 0, height = 0, horizontalScale = 0, verticalScale = 0;
            if (key)
            {
                ReadOnlySpan<byte> signature = [0x9d, 0x01, 0x2a];
                if (!encoded.Slice(3, 3).SequenceEqual(signature))
                { throw new InvalidDataException("Invalid VP8 keyframe start code."); }
                width = (encoded[6] | encoded[7] << 8) & 0x3fff;
                height = (encoded[8] | encoded[9] << 8) & 0x3fff;
                horizontalScale = encoded[7] >> 6; verticalScale = encoded[9] >> 6;
                if (width == 0 || height == 0) { throw new InvalidDataException("VP8 picture dimensions must be positive."); }
            }
            return new(key, (tag & 16) != 0, version, offset, length, width, height, horizontalScale, verticalScale);
        }
    }
}
