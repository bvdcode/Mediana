// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Core.Memory;
using Mediana.Core.Video;
namespace Mediana.Documents.Pdf
{
    /// <summary>A decoded image XObject with PDF color conversion and masks applied, independent of its placement.</summary>
    public class PdfImage
    {
        private readonly PixelBuffer _pixels;
        public string Name { get; }
        public int Width => _pixels.Width;
        public int Height => _pixels.Height;
        public ReadOnlyMemory<byte> Pixels => _pixels.Pixels;
        internal PixelBuffer Buffer => _pixels;
        internal PdfImage(string name, PixelBuffer pixels) { Name = name; _pixels = pixels; }
        public VideoFrame ToVideoFrame(MediaBufferAllocator allocator, CancellationToken cancellationToken = default) => _pixels.ToVideoFrame(allocator, cancellationToken);
    }
}
