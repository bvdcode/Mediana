// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Jpeg
{
    /// <summary>Owned native JPEG component samples, independent of encoded input and presentation metadata.</summary>
    public class JpegImage : IDisposable
    {
        private readonly JpegSampleLayout samples;
        private readonly MediaBuffer storage;
        private bool disposed;

        internal JpegImage(JpegDocument image)
        {
            AdobeColorTransform = image.Metadata.AdobeColorTransform;
            Warnings = image.Warnings;
            EncodedWidth = image.Width;
            EncodedHeight = image.Height;
            samples = image.TakeSamples(out storage);
        }

        /// <summary>Nonfatal decoding diagnostics.</summary>
        public IReadOnlyList<string> Warnings { get; }
        public int Width => samples.Width;
        public int Height => samples.Height;
        public int ComponentCount => samples.Components.Count;
        /// <summary>Width declared in the encoded JPEG frame, before reduction.</summary>
        public int EncodedWidth { get; }
        /// <summary>Height declared in the encoded JPEG frame, before reduction.</summary>
        public int EncodedHeight { get; }
        /// <summary>Original APP14 transform code: 0 (none), 1 (YCbCr), 2 (YCCK), or null when absent.</summary>
        public int? AdobeColorTransform { get; }

        internal JpegColorSpace GetComponentColorSpace() => (ComponentCount, AdobeColorTransform) switch
        {
            (4, 0) => JpegColorSpace.InvertedCmyk,
            (4, 2) => JpegColorSpace.Ycck,
            (4, null) => JpegColorSpace.Cmyk,
            (1 or 3, 0) => JpegColorSpace.Rgb,
            (1 or 3, 1) => JpegColorSpace.YCbCr,
            (3, null) when samples.Components[0].Id == (byte)'R' &&
                samples.Components[1].Id == (byte)'G' && samples.Components[2].Id == (byte)'B' => JpegColorSpace.Rgb,
            (1 or 3, null) => JpegColorSpace.YCbCr,
            _ => throw new InvalidDataException("The JPEG Adobe component transform conflicts with its component count.")
        };

        /// <summary>Writes straight RGBA8 into caller-owned storage using the explicit component interpretation.
        /// Grayscale accepts RGB or YCbCr interpretation. Four-component input requires CMYK or YCCK. Writes exactly Width*Height*4 bytes and
        /// preserves any destination tail. No output allocation or metadata-derived color conversion occurs.
        /// The caller keeps the destination valid; cancellation may leave a partially written destination.</summary>
        public void WriteRgbaTo(Span<byte> destination, JpegColorSpace colorSpace, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            int requiredComponents = colorSpace switch
            {
                JpegColorSpace.Rgb or JpegColorSpace.YCbCr => 3,
                JpegColorSpace.Cmyk or JpegColorSpace.InvertedCmyk or JpegColorSpace.Ycck => 4,
                _ => throw new ArgumentOutOfRangeException(nameof(colorSpace))
            };
            if (ComponentCount != requiredComponents && (ComponentCount != 1 || requiredComponents != 3))
            {
                throw new ArgumentException("JPEG component count differs from the requested color interpretation.", nameof(colorSpace));
            }
            JpegPixelWriter.Write(samples, destination, colorSpace, cancellationToken);
        }

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                foreach (JpegComponent component in samples.Components)
                {
                    component.Progression?.Dispose();
                }
                storage.Dispose();
            }
        }
    }
}
