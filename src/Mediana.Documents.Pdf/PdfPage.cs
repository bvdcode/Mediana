// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    /// <summary>A detached page owning drawing commands and decoded image XObjects. Dispose after rendering or extraction.</summary>
    public class PdfPage : IDisposable
    {
        private bool _disposed;
        public PdfPageInfo Info { get; }
        public double Width => Info.Width;
        public double Height => Info.Height;
        public int Number => Info.Number;
        public IReadOnlyList<string> UnsupportedFeatures { get; }
        /// <summary>Non-fatal diagnostics collected while interpreting page resources.</summary>
        public IReadOnlyList<string> Warnings { get; }
        public IReadOnlyList<PdfImage> Images { get; }
        internal IReadOnlyList<IPdfDrawCommand> Commands { get; }
        internal PdfPage(PdfPageInfo info, List<IPdfDrawCommand> commands, HashSet<string> unsupported, List<PdfImage> images, HashSet<string> warnings)
        { Info = info; Commands = commands.AsReadOnly(); UnsupportedFeatures = Array.AsReadOnly(unsupported.Order(StringComparer.Ordinal).ToArray()); Images = images.AsReadOnly(); Warnings = Array.AsReadOnly(warnings.Order(StringComparer.Ordinal).ToArray()); }
        internal void RequireOpen() => ObjectDisposedException.ThrowIf(_disposed, this);
        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            foreach (PdfImage image in Images) { image.Dispose(); }
        }
    }
}
