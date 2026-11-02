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
            VideoDisplayMatrix displayMatrix, ContainerPacketIndexInfo packetIndex,
            Mp4LanguageInfo language, Mp4HeaderDates headerDates, Mp4HeaderDates mediaDates, Mp4MetadataScope metadata,
            IReadOnlyList<VideoColorDescription>? colorDescriptions = null,
            IReadOnlyList<Mp4SampleRecoveryGroup>? recoveryGroups = null)
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
            ColorDescriptions = colorDescriptions ?? Array.Empty<VideoColorDescription>();
            RecoveryGroups = recoveryGroups ?? Array.Empty<Mp4SampleRecoveryGroup>();
            PacketIndex = packetIndex;
            AverageFrameRate = stream.Kind switch
            {
                MediaKind.Video when packetIndex.DecodeDuration is MediaDuration duration =>
                    FrameRate.FromFrameCount(packetIndex.PacketCount, duration),
                MediaKind.Video => null,
                MediaKind.Audio => null,
                MediaKind.Data => null,
                _ => throw new ArgumentException("The MP4 track kind is unsupported.", nameof(stream))
            };
            Language = language;
            HeaderDates = headerDates;
            MediaDates = mediaDates;
            MetadataScope = metadata;
            Metadata = Array.AsReadOnly(metadata.Values.Select(value => value.Item).ToArray());
            UserData = Array.AsReadOnly(metadata.UserData.Select(value => value.Item).ToArray());
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
        /// <summary>Complete sample-index totals, independent of mdhd/tkhd duration declarations and edit lists.</summary>
        public ContainerPacketIndexInfo PacketIndex { get; }
        /// <summary>Mean indexed AVC picture rate before edit-list translation, not a constant-cadence declaration.</summary>
        public FrameRate? AverageFrameRate { get; }
        public Mp4LanguageInfo Language { get; }
        /// <summary>Original tkhd creation/modification declarations, independent of media dates.</summary>
        public Mp4HeaderDates HeaderDates { get; }
        /// <summary>Original mdhd creation/modification declarations, independent of track dates.</summary>
        public Mp4HeaderDates MediaDates { get; }
        /// <summary>Track and media metadata in structural file order, retaining native source paths.</summary>
        public IReadOnlyList<Mp4MetadataItem> Metadata { get; }
        /// <summary>Recognized user-data names in structural order, with original payload access through the reader.</summary>
        public IReadOnlyList<Mp4UserDataItem> UserData { get; }
        internal Mp4MetadataScope MetadataScope { get; }
        /// <summary>The track header matrix before the movie matrix. Video Stream.DisplayMatrix composes both.</summary>
        public VideoDisplayMatrix DisplayMatrix { get; }
        /// <summary>Recognized source color descriptions in sample-entry order. Stream.ColorDescription is the selected representation.</summary>
        public IReadOnlyList<VideoColorDescription> ColorDescriptions { get; }

        /// <summary>Declared encoded-sample recovery requirements, independent of PCM edits and codec startup delay.</summary>
        public IReadOnlyList<Mp4SampleRecoveryGroup> RecoveryGroups { get; }

        internal Mp4TrackInfo WithStream(EncodedStreamInfo stream) => new(TrackId, stream, SampleEntry,
            MediaDuration, PresentationDuration, Edits, Width, Height, DeclaredAudioChannels,
            DeclaredAudioSampleRate, SampleCount, DisplayMatrix, PacketIndex, Language, HeaderDates, MediaDates, MetadataScope, ColorDescriptions, RecoveryGroups);
    }
}
