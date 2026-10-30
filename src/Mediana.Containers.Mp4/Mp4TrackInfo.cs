// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Containers.Mp4
{
    /// <summary>Container declarations. Codec configuration may provide different audio channel or sample rate values.
    /// Stream time base and media duration retain the original media timeline.</summary>
    public class Mp4TrackInfo
    {
        internal Mp4TrackInfo(uint trackId, EncodedStreamInfo stream, string sampleEntry,
            MediaDuration? mediaDuration, MediaDuration? presentationDuration, IReadOnlyList<Mp4Edit> edits,
            int? width, int? height, int? declaredAudioChannels, int? declaredAudioSampleRate, int sampleCount,
            VideoDisplayMatrix displayMatrix)
        {
            TrackId = trackId;
            Stream = stream;
            SampleEntry = sampleEntry;
            MediaDuration = mediaDuration;
            PresentationDuration = presentationDuration;
            Edits = edits;
            Width = width;
            Height = height;
            DeclaredAudioChannels = declaredAudioChannels;
            DeclaredAudioSampleRate = declaredAudioSampleRate;
            SampleCount = sampleCount;
            DisplayMatrix = displayMatrix;
        }

        public uint TrackId { get; }
        public EncodedStreamInfo Stream { get; }
        public string SampleEntry { get; }
        public MediaDuration? MediaDuration { get; }
        public MediaDuration? PresentationDuration { get; }
        public IReadOnlyList<Mp4Edit> Edits { get; }
        public int? Width { get; }
        public int? Height { get; }
        public int? DeclaredAudioChannels { get; }
        public int? DeclaredAudioSampleRate { get; }
        public int SampleCount { get; }
        /// <summary>The track header matrix before the movie matrix. Video Stream.DisplayMatrix composes both.</summary>
        public VideoDisplayMatrix DisplayMatrix { get; }

        internal Mp4TrackInfo WithStream(EncodedStreamInfo stream) => new(TrackId, stream, SampleEntry,
            MediaDuration, PresentationDuration, Edits, Width, Height, DeclaredAudioChannels,
            DeclaredAudioSampleRate, SampleCount, DisplayMatrix);
    }
}
