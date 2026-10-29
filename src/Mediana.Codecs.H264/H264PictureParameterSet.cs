// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal record H264PictureParameterSet(int Id, int SequenceId, bool Cabac,
        bool BottomFieldPictureOrderPresent, int InitialQp, int ChromaQpOffset, int SecondChromaQpOffset,
        bool DeblockingControlPresent, bool ConstrainedIntraPrediction, bool RedundantPictureCountPresent,
        bool Transform8x8, int DefaultReferencesL0, int DefaultReferencesL1, bool WeightedPrediction, int WeightedBiPrediction);
}
