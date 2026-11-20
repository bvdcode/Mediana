// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    public record H264DecoderOptions
    {
        public int MaxPicturePixels { get; init; } = 4096 * 4096;
        public int MaxConfigurationBytes { get; init; } = H264ConfigurationRecord.DefaultMaxConfigurationBytes;
        public int MaxParameterSetBytes { get; init; } = H264ConfigurationRecord.DefaultMaxParameterSetBytes;
        public int MaxNalsPerPacket { get; init; } = 1024;
        public int MaxReferenceOperations { get; init; } = 64;

        /// <summary>Ignore optional picture-timing hints that contradict coded picture structure or order.
        /// Presentation is inferred from the coded picture; the decoder reports the first occurrence through its warning handler.</summary>
        public bool IgnoreInconsistentPictureTiming { get; init; }

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
