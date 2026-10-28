namespace Mediana.Codecs.H264
{
    internal readonly record struct H264PictureBufferLimits(int ReorderFrames, int DecodedFrames)
    {
        public static H264PictureBufferLimits Read(H264SequenceParameterSet sequence)
        {
            int maximumMbs = LevelMacroblocks(sequence)
                ?? throw new NotSupportedException("The H.264 level has no supported decoded picture buffer limits.");
            int pictureMbs = checked(sequence.WidthInMbs * sequence.HeightInMapUnits * (sequence.FrameMbsOnly ? 1 : 2));
            if (pictureMbs <= 0 || pictureMbs > maximumMbs)
            {
                throw new InvalidDataException("The H.264 picture exceeds its level's decoded picture buffer.");
            }
            int maximumFrames = Math.Min(maximumMbs / pictureMbs, 16);
            bool intraOnly = sequence.Profile is 44 or 86 or 100 or 110 or 122 or 244 && (sequence.Constraints & 16) != 0;
            int inferredFrames = intraOnly ? 0 : maximumFrames;
            int buffering = sequence.Vui?.MaxDecodedFrameBuffering ?? inferredFrames;
            int reorder = sequence.Vui?.MaxReorderFrames ?? inferredFrames;
            if (buffering < sequence.ReferenceFrames || buffering > maximumFrames || reorder < 0 || reorder > buffering)
            {
                throw new InvalidDataException("The H.264 reference and output buffering limits exceed the declared level.");
            }
            // Rec. ITU-T H.264, 8.2.1.3: type-two output order equals decoding order.
            return new H264PictureBufferLimits(sequence.PictureOrderType == 2 ? 0 : reorder, Math.Max(1, buffering));
        }

        // Rec. ITU-T H.264, Table A-1, MaxDpbMbs.
        public static int? LevelMacroblocks(H264SequenceParameterSet sequence) => sequence.Level switch
        {
            9 or 10 => 396,
            11 when sequence.Profile is 66 or 77 or 88 && (sequence.Constraints & 16) != 0 => 396,
            11 => 900,
            12 or 13 or 20 => 2376,
            21 => 4752,
            22 or 30 => 8100,
            31 => 18000,
            32 => 20480,
            40 or 41 => 32768,
            42 => 34816,
            50 => 110400,
            51 or 52 => 184320,
            60 or 61 or 62 => 696320,
            _ => null
        };
    }
}
