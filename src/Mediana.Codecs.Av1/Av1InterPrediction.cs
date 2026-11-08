// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1InterPrediction(Av1ReferenceIndices References, int PrimaryReference,
        bool IntegerMotion, bool HighPrecisionMotion, int InterpolationFilter, bool SwitchableMotion, bool ReferenceMotion, bool WarpedMotion = false)
    {
        internal const int NoPrimaryReference = 7;
    }
}
