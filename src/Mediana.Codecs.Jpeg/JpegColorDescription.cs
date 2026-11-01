// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.Jpeg
{
    /// <summary>Records an embedded JPEG ICC profile without retaining the profile bytes.</summary>
    public class JpegColorDescription : VideoColorDescription
    {
        public override VideoColorInfo Resolve(VideoColorInfo codecColor)
        {
            ArgumentNullException.ThrowIfNull(codecColor);
            throw new NotSupportedException("JPEG color conversion requires support for the embedded ICC profile.");
        }
    }
}
