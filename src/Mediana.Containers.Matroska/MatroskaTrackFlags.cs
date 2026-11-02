// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    public readonly record struct MatroskaTrackFlags(bool Enabled, bool Default, bool Forced, bool Lacing);
}
