// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal readonly record struct AacAccessUnitInspection(bool Independent, bool HasSbr, bool HasParametricStereo);
}
