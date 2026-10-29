// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Core
{
    /// <summary>Immutable stream configuration. Codec identifiers are independent of implementations.</summary>
    public class EncodedStreamInfo
    {
        public EncodedStreamInfo(int streamIndex, MediaKind kind, string codecId, TimeBase timeBase,
            ReadOnlySpan<byte> configuration = default, VideoDisplayMatrix? displayMatrix = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            ArgumentException.ThrowIfNullOrWhiteSpace(codecId);
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            timeBase.Validate();
            displayMatrix?.Validate();
            if (displayMatrix is not null && kind != MediaKind.Video)
            {
                throw new ArgumentException("A video display matrix requires a video stream.", nameof(displayMatrix));
            }
            StreamIndex = streamIndex;
            Kind = kind;
            CodecId = codecId;
            TimeBase = timeBase;
            Configuration = configuration.ToArray();
            DisplayMatrix = displayMatrix;
        }

        public int StreamIndex { get; }
        public MediaKind Kind { get; }
        public string CodecId { get; }
        public TimeBase TimeBase { get; }
        public ReadOnlyMemory<byte> Configuration { get; }
        /// <summary>Optional container display transform. Null means unspecified; decoding does not apply it.</summary>
        public VideoDisplayMatrix? DisplayMatrix { get; }
    }
}
