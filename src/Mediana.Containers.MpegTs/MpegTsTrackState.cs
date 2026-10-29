// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Containers.MpegTs
{
    internal readonly record struct MpegTsTrackState(EncodedStreamInfo Stream, int Counter, long? LastDecodeTime);
}
