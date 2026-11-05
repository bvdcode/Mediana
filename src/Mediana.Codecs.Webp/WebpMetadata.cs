// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Webp
{
    /// <summary>Borrowed raw metadata payloads. The ICC profile must describe the supplied pixels; encoding does not convert profiles.
    /// EXIF and XMP are carried unchanged. The caller keeps these memories unchanged until encoding completes.</summary>
    public record WebpMetadata
    {
        public ReadOnlyMemory<byte> IccProfile { get; init; }
        public ReadOnlyMemory<byte> Exif { get; init; }
        public ReadOnlyMemory<byte> Xmp { get; init; }
    }
}
