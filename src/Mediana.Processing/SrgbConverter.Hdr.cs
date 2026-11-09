// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    public static partial class SrgbConverter
    {
        /// <summary>Converts to sRGB with explicit Hable tone mapping for PQ/HLG input.
        /// SDR and embedded ICC input follow the normal conversion path. Does not change source metadata.</summary>
        public static VideoFrame ApplyWithColorPolicy(VideoFrame input, Func<VideoFormat, VideoColorInfo>? colorResolver,
            HdrToneMappingOptions toneMapping, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(toneMapping);
            toneMapping.Validate();
            NearestNeighborMap map = new(input.Format.Width, input.Format.Height, input.Format.Width, input.Format.Height,
                OrthogonalTransform.Identity);
            return ApplyWithColorPolicy(input, colorResolver, map, input.Format.SampleAspectRatio, allocator, cancellationToken, toneMapping);
        }
    }
}
