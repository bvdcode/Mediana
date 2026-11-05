// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Codecs;
using Mediana.Imaging;
using System;
using System.Collections.Generic;

namespace Mediana.Images.Svg
{
    /// <summary>Bounds for XML input and interpreted geometry. External entities/resources are disabled.</summary>
    public record SvgReaderOptions
    {
        /// <summary>Gets the maximum encoded input length; strings are counted as UTF-8. Defaults to 16 MiB.</summary>
        public int MaximumInputBytes { get; init; } = 16 * 1024 * 1024;
        /// <summary>Gets the maximum XML element and expanded-reference count.</summary>
        public int MaximumElements { get; init; } = 100_000;
        /// <summary>Gets the maximum stored path commands across expanded nodes.</summary>
        public int MaximumPathCommands { get; init; } = 1_000_000;
        /// <summary>Gets the maximum XML/reference nesting depth, up to 64.</summary>
        public int MaximumDepth { get; init; } = 64;
        /// <summary>Gets the decoded pixel budget per embedded or resolved image.</summary>
        public int MaximumImagePixels { get; init; } = 16_777_216;
        /// <summary>Live decoded image and decoder scanline budget when ImageAllocator is absent.</summary>
        public long MaximumImageBytes { get; init; } = 256L * 1024 * 1024;
        /// <summary>Optional budget for retained images and decode workspace. Registered decoding requires a scope of its operation budget.</summary>
        public MediaBufferAllocator? ImageAllocator { get; init; }
        /// <summary>Decoders shared by embedded resources. Registered decoding uses the engine's context.</summary>
        public ImageDecodeContext Images { get; init; } = ImageResources.DefaultDecoders;
        /// <summary>Optional explicit interpretation of missing source color labels. Declared labels remain authoritative.</summary>
        public Func<VideoFormat, VideoColorInfo>? ImageColorResolver { get; init; }
        public Action<string>? WarningHandler { get; init; }
        /// <summary>Shared text resources. The default uses the bundled CC0 sans-serif and supplemental faces.</summary>
        public TextOptions Text { get; init; } = new() { SubstituteMissingFonts = true, SubstituteFamilies = ["Aileron", "Canada1500"] };
        /// <summary>Gets an optional caller-controlled image resolver. No network or file access is performed implicitly.</summary>
        public Func<string, PixelBuffer?>? ImageResolver { get; init; }
    }
}
