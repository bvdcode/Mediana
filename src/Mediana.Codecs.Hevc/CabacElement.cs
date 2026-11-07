// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal enum CabacElement
    {
        SplitCodingUnit,
        TransquantBypass,
        PartMode,
        PreviousIntraLuma,
        ChromaMode,
        SplitTransform,
        LumaCbf,
        ChromaCbf,
        QpDelta,
        TransformSkip,
        LastX,
        LastY,
        CodedSubBlock,
        SignificantCoefficient,
        GreaterOne,
        GreaterTwo,
        SaoMerge,
        SaoType,
        Skip,
        PredictionMode,
        RootCbf,
        Merge,
        MergeIndex,
        InterDirection,
        ReferenceIndex,
        MotionPredictor,
        MotionDifference
    }
}
