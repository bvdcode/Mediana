// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.Png
{
    /// <summary>PNG color declarations requiring a color transform beyond the available CICP/sRGB interpretation.
    /// ICC presence is recorded without decompressing or retaining its profile. Chromaticities use PNG's scale of 100000.</summary>
    public class PngColorDescription : VideoColorDescription
    {
        internal PngColorDescription(uint? gamma, ReadOnlySpan<uint> chromaticities, bool embeddedIcc)
        {
            Gamma = gamma;
            Chromaticities = Array.AsReadOnly(chromaticities.ToArray());
            HasEmbeddedIccProfile = embeddedIcc;
        }

        public uint? Gamma { get; }
        /// <summary>White x/y, red x/y, green x/y, blue x/y; empty when cHRM is absent.</summary>
        public IReadOnlyList<uint> Chromaticities { get; }
        public bool HasEmbeddedIccProfile { get; }

        public override VideoColorInfo Resolve(VideoColorInfo codecColor)
        {
            ArgumentNullException.ThrowIfNull(codecColor);
            throw new NotSupportedException("Color conversion requires support for the declared PNG gamma, chromaticities or ICC profile.");
        }
    }
}
