namespace Mediana.Codecs.H264
{
    internal record H264SequenceParameterSet
    {
        public int Id { get; init; }
        public int Profile { get; init; }
        public int Constraints { get; init; }
        public int Level { get; init; }
        public int ChromaFormat { get; init; } = 1;
        public bool SeparateColourPlane { get; init; }
        public int LumaBitDepth { get; init; } = 8;
        public int ChromaBitDepth { get; init; } = 8;
        public bool TransformBypass { get; init; }
        public bool ScalingMatrix { get; init; }
        public int FrameNumberBits { get; init; }
        public int PictureOrderType { get; init; }
        public int PictureOrderBits { get; init; }
        public bool DeltaPictureOrderAlwaysZero { get; init; }
        public int NonReferencePictureOrderOffset { get; init; }
        public int TopToBottomPictureOrderOffset { get; init; }
        public H264PictureOrderCycle PictureOrderCycle { get; init; } = H264PictureOrderCycle.Empty;
        public int ReferenceFrames { get; init; }
        public bool FrameNumberGapsAllowed { get; init; }
        public H264VuiParameters? Vui { get; init; }
        public int WidthInMbs { get; init; }
        public int HeightInMapUnits { get; init; }
        public bool FrameMbsOnly { get; init; }
        public bool MbAdaptiveFrameField { get; init; }
        public int CropLeft { get; init; }
        public int CropTop { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public int CodedWidth => checked(WidthInMbs * 16);
        public int CodedHeight => checked(HeightInMapUnits * (FrameMbsOnly ? 16 : 32));
    }
}
