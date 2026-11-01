// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    internal readonly record struct H264VuiSyntax(int VuiFlag, int AspectEnd, int SignalStart, int SignalEnd, int PayloadEnd);
}
