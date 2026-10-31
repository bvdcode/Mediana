// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Core
{
    /// <summary>Immutable stream configuration. Codec identifiers are independent of implementations.</summary>
    public class EncodedStreamInfo
    {
        public EncodedStreamInfo(int streamIndex, MediaKind kind, string codecId, EncodedStreamFormat format, TimeBase timeBase,
            ReadOnlySpan<byte> configuration = default, VideoDisplayMatrix? displayMatrix = null,
            MediaDuration? codedDuration = null, VideoColorDescription? colorDescription = null,
            VideoAspectRatioDescription? aspectRatioDescription = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            ArgumentException.ThrowIfNullOrWhiteSpace(codecId);
            ArgumentNullException.ThrowIfNull(format);
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            timeBase.Validate();
            displayMatrix?.Validate();
            codedDuration?.TimeBase.Validate();
            if (displayMatrix is not null && kind != MediaKind.Video)
            {
                throw new ArgumentException("A video display matrix requires a video stream.", nameof(displayMatrix));
            }
            if (colorDescription is not null && kind != MediaKind.Video)
            {
                throw new ArgumentException("A color description requires a video stream.", nameof(colorDescription));
            }
            if (aspectRatioDescription is not null && kind != MediaKind.Video)
            {
                throw new ArgumentException("An aspect ratio description requires a video stream.", nameof(aspectRatioDescription));
            }
            StreamIndex = streamIndex;
            Kind = kind;
            CodecId = codecId;
            Format = format;
            TimeBase = timeBase;
            Configuration = configuration.ToArray();
            DisplayMatrix = displayMatrix;
            CodedDuration = codedDuration;
            ColorDescription = colorDescription;
            AspectRatioDescription = aspectRatioDescription;
        }

        public int StreamIndex { get; }
        public MediaKind Kind { get; }
        public string CodecId { get; }
        public EncodedStreamFormat Format { get; }
        public TimeBase TimeBase { get; }
        public ReadOnlyMemory<byte> Configuration { get; }
        /// <summary>Optional container display transform. Null means unspecified; decoding does not apply it.</summary>
        public VideoDisplayMatrix? DisplayMatrix { get; }
        /// <summary>Total coded media duration, including codec delay/padding and excluding container edits. Null means unknown.</summary>
        public MediaDuration? CodedDuration { get; }
        /// <summary>Optional source color description, independent of codec configuration.</summary>
        public VideoColorDescription? ColorDescription { get; }
        /// <summary>Optional source pixel geometry, independent of codec configuration.</summary>
        public VideoAspectRatioDescription? AspectRatioDescription { get; }
    }
}
