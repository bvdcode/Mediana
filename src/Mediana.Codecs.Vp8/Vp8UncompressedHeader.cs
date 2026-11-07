// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Video;

namespace Mediana.Codecs.Vp8
{
    internal readonly record struct Vp8UncompressedHeader(bool KeyFrame, bool ShowFrame, int Version,
        int ControlOffset, int ControlLength, int Width, int Height, int HorizontalScale, int VerticalScale)
    {
        internal static Vp8UncompressedHeader Parse(ReadOnlySpan<byte> encoded)
        {
            Vp8FrameTag tag = Vp8FrameTag.Read(encoded, encoded.Length);
            bool key = tag.KeyFrame;
            int offset = key ? 10 : Vp8FrameTag.HeaderBytes;
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
            return new(key, tag.ShowFrame, tag.Version, offset, tag.ControlLength, width, height, horizontalScale, verticalScale);
        }
    }
}
