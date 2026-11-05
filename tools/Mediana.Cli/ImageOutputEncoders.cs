// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Png;
using Mediana.Codecs.Webp;

namespace Mediana.Cli
{
    internal static class ImageOutputEncoders
    {
        private static readonly IReadOnlyDictionary<string, IImageEncoder> Encoders = new Dictionary<string, IImageEncoder>(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = new PngImageEncoder(),
            [".webp"] = new WebpImageEncoder()
        };

        internal static IImageEncoder ForPath(string path) => Encoders.TryGetValue(Path.GetExtension(path), out IImageEncoder? encoder)
            ? encoder : throw new NotSupportedException("Image output requires a registered extension: .png or .webp.");
    }
}
