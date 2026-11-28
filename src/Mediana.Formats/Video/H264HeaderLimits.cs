// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    internal readonly record struct H264HeaderLimits(int? MaxPicturePixels, int MaxConfigurationBytes, int MaxParameterSetBytes)
    {
        public const int DefaultMaxPicturePixels = 4096 * 4096;
        public const int DefaultMaxConfigurationBytes = H264ConfigurationRecord.DefaultMaxConfigurationBytes;
        public const int DefaultMaxParameterSetBytes = H264ConfigurationRecord.DefaultMaxParameterSetBytes;
        public const int DefaultMaxNalsPerPacket = 1024;
        public const int DefaultMaxReferenceOperations = 64;

        public static H264HeaderLimits Default => new(DefaultMaxPicturePixels, DefaultMaxConfigurationBytes, DefaultMaxParameterSetBytes);
    }
}
