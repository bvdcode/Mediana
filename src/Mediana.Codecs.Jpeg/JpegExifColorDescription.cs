// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.Jpeg
{
    /// <summary>Records explicitly uncalibrated Exif color without substituting a display color space.</summary>
    public class JpegExifColorDescription : VideoColorDescription
    {
        public override VideoColorInfo Resolve(VideoColorInfo codecColor)
        {
            ArgumentNullException.ThrowIfNull(codecColor);
            throw new NotSupportedException("JPEG color conversion requires a calibrated interpretation of the Exif color space.");
        }
    }
}
