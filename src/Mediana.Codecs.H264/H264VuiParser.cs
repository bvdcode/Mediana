namespace Mediana.Codecs.H264
{
    internal static class H264VuiParser
    {
        public static H264VuiParameters Parse(ref H264BitReader bits, int referenceFrames)
        {
            H264VuiParameters result = new();
            if (bits.ReadFlag())
            {
                int aspectRatioIdc = (int)bits.ReadBits(8);
                (int width, int height) = aspectRatioIdc switch
                {
                    0 => (0, 0),
                    1 => (1, 1),
                    2 => (12, 11),
                    3 => (10, 11),
                    4 => (16, 11),
                    5 => (40, 33),
                    6 => (24, 11),
                    7 => (20, 11),
                    8 => (32, 11),
                    9 => (80, 33),
                    10 => (18, 11),
                    11 => (15, 11),
                    12 => (64, 33),
                    13 => (160, 99),
                    14 => (4, 3),
                    15 => (3, 2),
                    16 => (2, 1),
                    255 => ((int)bits.ReadBits(16), (int)bits.ReadBits(16)),
                    _ => throw new InvalidDataException("The H.264 aspect ratio identifier is reserved.")
                };
                result = result with { AspectRatioWidth = width, AspectRatioHeight = height };
            }
            if (bits.ReadFlag())
            {
                result = result with { OverscanAppropriate = bits.ReadFlag() };
            }
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
