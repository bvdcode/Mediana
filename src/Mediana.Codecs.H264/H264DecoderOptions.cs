// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    public record H264DecoderOptions
    {
        public int MaxPicturePixels { get; init; } = H264HeaderLimits.DefaultMaxPicturePixels;
        public int MaxConfigurationBytes { get; init; } = H264HeaderLimits.DefaultMaxConfigurationBytes;
        public int MaxParameterSetBytes { get; init; } = H264HeaderLimits.DefaultMaxParameterSetBytes;
        public int MaxNalsPerPacket { get; init; } = H264HeaderLimits.DefaultMaxNalsPerPacket;
        public int MaxReferenceOperations { get; init; } = H264HeaderLimits.DefaultMaxReferenceOperations;

        /// <summary>Ignore optional picture-timing hints that contradict coded picture structure or order.
        /// Presentation is inferred from the coded picture; the decoder reports the first occurrence through its warning handler.</summary>
        public bool IgnoreInconsistentPictureTiming { get; init; }

        internal H264HeaderLimits HeaderLimits => new(MaxPicturePixels, MaxConfigurationBytes, MaxParameterSetBytes);

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxPicturePixels);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxConfigurationBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxParameterSetBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxNalsPerPacket);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxReferenceOperations);
        }
    }
}
