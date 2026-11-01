// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Containers.Matroska
{
    /// <summary>Native track declarations with format defaults applied. Independent of codec-derived metadata.</summary>
    public class MatroskaTrackInfo
    {
        internal MatroskaTrackInfo(MatroskaTrack track, EncodedStreamInfo stream)
        {
            Number = track.Number;
            Uid = track.Uid;
            Type = track.Type;
            CodecId = track.CodecId;
            Flags = track.Flags;
            Timing = track.Timing;
            Labels = track.Labels;
            Video = track.Video;
            Audio = track.Audio;
            HasContentEncodings = track.ContentEncodings is not null;
            HasTrackOperation = track.TrackOperation is not null;
            Stream = stream;
        }

        /// <summary>Native positive TrackNumber used in Block headers, independent of the zero-based stream index.</summary>
        public ulong Number { get; }
        public ulong Uid { get; }
        public ulong Type { get; }
        /// <summary>Native CodecID. Stream.CodecId is normalized only when the stored representation can be mapped.</summary>
        public string CodecId { get; }
        public MatroskaTrackFlags Flags { get; }
        public MatroskaTrackTiming Timing { get; }
        public MatroskaTrackLabels Labels { get; }
        public MatroskaVideoSettings? Video { get; }
        public MatroskaAudioSettings? Audio { get; }
        public bool HasContentEncodings { get; }
        public bool HasTrackOperation { get; }
        public EncodedStreamInfo Stream { get; }
    }
}
