// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    /// <summary>Bounds encoded header parsing. Inspection allocates no decoded picture or reference surfaces.</summary>
    public record H264MetadataOptions
    {
        public int MaxConfigurationBytes { get; init; } = H264ConfigurationRecord.DefaultMaxConfigurationBytes;
        public int MaxParameterSetBytes { get; init; } = H264ConfigurationRecord.DefaultMaxParameterSetBytes;

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxConfigurationBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxParameterSetBytes);
        }
    }
}
