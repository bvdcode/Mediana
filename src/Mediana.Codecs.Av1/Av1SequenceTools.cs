// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1SequenceTools(bool ReducedStillPicture, int WidthBits, int HeightBits,
        int OrderHintBits, int ForceScreenContent, int ForceIntegerMotion, int Level, bool Tier, int FrameIdBits = 0, int DeltaFrameIdBits = 0,
        bool InterIntra = false, bool MaskedCompound = false, bool WarpedMotion = false, bool DualFilter = false,
        bool JointCompound = false, bool ReferenceMotion = false);
}
