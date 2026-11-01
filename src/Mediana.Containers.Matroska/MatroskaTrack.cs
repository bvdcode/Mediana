// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal record MatroskaTrack(ulong Number, ulong Uid, ulong Type, string CodecId,
        MatroskaTrackFlags Flags, MatroskaTrackTiming Timing, MatroskaTrackLabels Labels,
        EbmlElement? CodecPrivate, MatroskaVideoSettings? Video, MatroskaAudioSettings? Audio,
        EbmlElement? ContentEncodings, EbmlElement? TrackOperation);
}
