namespace Mediana.Codecs.H264
{
    internal record H264SliceHeader(H264SequenceParameterSet Sequence, H264PictureParameterSet Picture)
    {
        public H264SliceKind Kind { get; init; }
        public bool IsIdr { get; init; }
        public int ReferenceIndicator { get; init; }
        public int FirstMacroblock { get; init; }
        public int FrameNumber { get; init; }
        public int? IdrPictureId { get; init; }
        public int PictureOrderLsb { get; init; }
        public int DeltaPictureOrderBottom { get; init; }
        public int DeltaPictureOrder0 { get; init; }
        public int DeltaPictureOrder1 { get; init; }
        public bool DirectSpatialMotionPrediction { get; init; }
        public int ActiveReferencesL0 { get; init; }
        public int ActiveReferencesL1 { get; init; }
        public IReadOnlyList<H264ReferenceModification> ReferenceModifications { get; init; } = [];
        public IReadOnlyList<H264ReferenceModification> ReferenceModificationsL1 { get; init; } = [];
        public H264WeightTable? Weights { get; init; }
        public H264WeightTable? WeightsL1 { get; init; }
        public bool NoOutputOfPriorPictures { get; init; }
        public bool LongTermReference { get; init; }
        public bool AdaptiveMarking { get; init; }
        public IReadOnlyList<H264MemoryOperation> MemoryOperations { get; init; } = [];
        public int CabacInitIdc { get; init; }
        public int Qp { get; init; }
        public int DeblockingIdc { get; init; }
        public int AlphaOffset { get; init; }
        public int BetaOffset { get; init; }
        public bool DeblockingEnabled => DeblockingIdc != 1;
    }
}
