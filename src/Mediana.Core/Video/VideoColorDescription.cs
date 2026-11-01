// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    /// <summary>An immutable source color description that takes precedence over codec color labels.</summary>
    public abstract class VideoColorDescription
    {
        /// <summary>Resolves color labels without changing sample values. Unsupported descriptions must be rejected.</summary>
        public abstract VideoColorInfo Resolve(VideoColorInfo codecColor);
    }
}
