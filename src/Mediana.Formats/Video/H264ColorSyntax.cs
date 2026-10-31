// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    internal readonly record struct H264ColorSyntax(int VuiFlag, int SignalStart, int SignalEnd, int PayloadEnd);
}
