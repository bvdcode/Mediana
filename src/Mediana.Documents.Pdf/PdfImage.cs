// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Core.Video;
namespace Mediana.Documents.Pdf
{
    /// <summary>A decoded image XObject with PDF color conversion and masks applied, independent of its placement.</summary>
    public class PdfImage : IDisposable
    {
        private readonly VideoFrame _frame;
        public string Name { get; }
        public int Width => _frame.Format.Width;
        public int Height => _frame.Format.Height;
        public ReadOnlyMemory<byte> Pixels => _frame.Data;
        internal VideoFrame Frame => _frame;
        internal PdfImage(string name, PixelBuffer pixels)
        {
            Name = name;
            _frame = pixels.PublishVideoFrame();
        }

        /// <summary>Retains immutable pixels without copying or transferring the page's ownership.
        /// The returned frame survives page disposal and keeps the source allocation budget until its last lease is released.</summary>
        public VideoFrame ToVideoFrame(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _frame.Retain();
        }
        public void Dispose() => _frame.Dispose();
    }
}
