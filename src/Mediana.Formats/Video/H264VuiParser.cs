// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Formats.Video
{
    /// <summary>Reads VUI declarations and validates bounded timing, HRD and picture-buffering syntax.</summary>
    public static class H264VuiParser
    {
        public static H264VuiParameters Parse(ref H264BitReader bits, int referenceFrames)
            => Parse(ref bits, referenceFrames, out _, out _, out _);

        internal static H264VuiParameters Parse(ref H264BitReader bits, int referenceFrames, out int aspectEnd, out int signalStart, out int signalEnd)
        {
            H264VuiParameters result = new();
            if (bits.ReadFlag())
            {
                int aspectRatioIdc = (int)bits.ReadBits(8);
                int width, height;
                if (aspectRatioIdc == 255) { width = (int)bits.ReadBits(16); height = (int)bits.ReadBits(16); }
                else
                {
                    SampleAspectRatio? ratio = SampleAspectRatioCode.Get(aspectRatioIdc);
                    width = ratio?.Numerator ?? 0; height = ratio?.Denominator ?? 0;
                }
                result = result with { AspectRatioWidth = width, AspectRatioHeight = height };
            }
            aspectEnd = bits.BitPosition;
            if (bits.ReadFlag())
            {
                result = result with { OverscanAppropriate = bits.ReadFlag() };
            }
            signalStart = bits.BitPosition;
            if (bits.ReadFlag())
            {
                int videoFormat = (int)bits.ReadBits(3);
                if (videoFormat > 5)
                {
                    throw new InvalidDataException("The H.264 video format is reserved.");
                }
                result = result with { VideoFormat = videoFormat, FullRange = bits.ReadFlag() };
                if (bits.ReadFlag())
                {
                    result = result with
                    {
                        ColorPrimaries = (int)bits.ReadBits(8),
                        TransferCharacteristics = (int)bits.ReadBits(8),
                        MatrixCoefficients = (int)bits.ReadBits(8)
                    };
                }
            }
            signalEnd = bits.BitPosition;
            if (bits.ReadFlag())
            {
                result = result with { ChromaLocationTop = bits.ReadUe(5), ChromaLocationBottom = bits.ReadUe(5) };
            }
            if (bits.ReadFlag())
            {
                uint units = bits.ReadBits(32);
                uint timeScale = bits.ReadBits(32);
                if (units == 0 || timeScale == 0)
                {
                    throw new InvalidDataException("H.264 VUI timing values must be nonzero.");
                }
                result = result with { UnitsInTick = units, TimeScale = timeScale, FixedFrameRate = bits.ReadFlag() };
            }
            bool nalHrd = bits.ReadFlag();
            if (nalHrd)
            {
                ReadHrd(ref bits);
            }
            bool vclHrd = bits.ReadFlag();
            if (vclHrd)
            {
                ReadHrd(ref bits);
            }
            if (nalHrd || vclHrd)
            {
                bits.ReadFlag();
            }
            result = result with { PictureStructurePresent = bits.ReadFlag() };
            if (bits.ReadFlag())
            {
                bits.ReadFlag();
                bits.ReadUe(16);
                bits.ReadUe(16);
                int horizontal = bits.ReadUe(15);
                int vertical = bits.ReadUe(15);
                int reorder = bits.ReadUe(16);
                int buffering = bits.ReadUe(16);
                if (reorder > buffering || buffering < referenceFrames)
                {
                    throw new InvalidDataException("The H.264 VUI picture buffering limits are inconsistent.");
                }
                result = result with
                {
                    MaxMotionLengthHorizontal = horizontal,
                    MaxMotionLengthVertical = vertical,
                    MaxReorderFrames = reorder,
                    MaxDecodedFrameBuffering = buffering
                };
            }
            return result;
        }

        private static void ReadHrd(ref H264BitReader bits)
        {
            int cpbCount = bits.ReadUe(31) + 1;
            bits.ReadBits(4);
            bits.ReadBits(4);
            for (int index = 0; index < cpbCount; index++)
            {
                bits.ReadUe();
                bits.ReadUe();
                bits.ReadFlag();
            }
            bits.ReadBits(5);
            bits.ReadBits(5);
            bits.ReadBits(5);
            bits.ReadBits(5);
        }
    }
}
