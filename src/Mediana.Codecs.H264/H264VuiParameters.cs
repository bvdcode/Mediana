namespace Mediana.Codecs.H264
{
    internal record H264VuiParameters
    {
        public int AspectRatioWidth { get; init; }
        public int AspectRatioHeight { get; init; }
        public bool? OverscanAppropriate { get; init; }
        public int VideoFormat { get; init; } = 5;
        public bool FullRange { get; init; }
        public int ColorPrimaries { get; init; } = 2;
        public int TransferCharacteristics { get; init; } = 2;
        public int MatrixCoefficients { get; init; } = 2;
        public int? ChromaLocationTop { get; init; }
        public int? ChromaLocationBottom { get; init; }
        public uint? UnitsInTick { get; init; }
        public uint? TimeScale { get; init; }
        public bool FixedFrameRate { get; init; }
        public bool PictureStructurePresent { get; init; }
        public int MaxMotionLengthHorizontal { get; init; } = 15;
        public int MaxMotionLengthVertical { get; init; } = 15;
        public int? MaxReorderFrames { get; init; }
        public int? MaxDecodedFrameBuffering { get; init; }
    }
}
