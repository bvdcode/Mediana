// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using Mediana.Core.Video;

namespace Mediana.Images.Heif
{
    /// <summary>
    /// Identifies the primaries and transfer function of the returned RGB signal using CICP codes.
    /// </summary>
    /// <param name="colorPrimaries">The signalled colour primaries code; 2 means unspecified.</param>
    /// <param name="transferCharacteristics">The signalled transfer characteristics code; 2 means unspecified.</param>
    /// <remarks>
    /// RGB values preserve the encoded primaries and transfer function. No gamut conversion,
    /// ICC processing or tone mapping is applied; the bytes are not implicitly sRGB.
    /// </remarks>
    public class ImageColorInformation(int colorPrimaries, int transferCharacteristics)
    {
        internal ImageColorInformation(int colorPrimaries, int transferCharacteristics, IccColorDescription? iccDescription)
            : this(colorPrimaries, transferCharacteristics)
        {
            IccDescription = iccDescription;
        }

        internal bool Matches(ImageColorInformation other) => (ColorPrimaries == other.ColorPrimaries) &&
            (TransferCharacteristics == other.TransferCharacteristics) && IccDescription?.Restricted == other.IccDescription?.Restricted &&
            IccProfile.Span.SequenceEqual(other.IccProfile.Span);

        internal IccColorDescription? IccDescription { get; }

        /// <summary>
        /// Gets an owned copy of the embedded RGB ICC profile, or empty memory when absent.
        /// </summary>
        /// <remarks>
        /// The profile describes the returned RGB signal and is preserved without applying a colour transform.
        /// </remarks>
        public ReadOnlyMemory<byte> IccProfile => IccDescription?.Profile ?? ReadOnlyMemory<byte>.Empty;

        /// <summary>
        /// Gets the CICP colour primaries code.
        /// </summary>
        public int ColorPrimaries { get; } = colorPrimaries;

        /// <summary>
        /// Gets the CICP transfer characteristics code.
        /// </summary>
        public int TransferCharacteristics { get; } = transferCharacteristics;
    }
}
