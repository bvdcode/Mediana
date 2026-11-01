// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg
{
    /// <summary>Owned native JPEG component samples, independent of encoded input and presentation metadata.</summary>
    public class JpegImage : IDisposable
    {
        private readonly JpegSampleLayout samples;
        private bool disposed;

        internal JpegImage(JpegDocument image)
        {
            AdobeColorTransform = image.Metadata.AdobeColorTransform;
            samples = image.TakeSamples();
        }

        public int Width => samples.Width;
        public int Height => samples.Height;
        public int ComponentCount => samples.Components.Count;
        /// <summary>Original APP14 transform code: 0 (none), 1 (YCbCr), 2 (YCCK), or null when absent.</summary>
        public int? AdobeColorTransform { get; }

        /// <summary>Writes straight RGBA8 into caller-owned storage using the explicit component interpretation.
        /// Grayscale ignores the three-component interpretation. Writes exactly Width*Height*4 bytes and
        /// preserves any destination tail. No output allocation or metadata-derived color conversion occurs.
        /// The caller keeps the destination valid; cancellation may leave a partially written destination.</summary>
        public void WriteRgbaTo(Span<byte> destination, JpegColorSpace colorSpace, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            bool rgb = colorSpace switch
            {
                JpegColorSpace.Rgb => true,
                JpegColorSpace.YCbCr => false,
                _ => throw new ArgumentOutOfRangeException(nameof(colorSpace))
            };
            JpegPixelWriter.Write(samples, destination, rgb, cancellationToken);
        }

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                foreach (JpegComponent component in samples.Components)
                {
                    component.Samples?.Dispose();
                    component.Progression?.Dispose();
                }
            }
        }
    }
}
