// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    /// <summary>A detached page, including ordered drawing commands and decoded image XObjects.</summary>
    public class PdfPage
    {
        public PdfPageInfo Info { get; }
        public double Width => Info.Width;
        public double Height => Info.Height;
        public int Number => Info.Number;
        public IReadOnlyList<string> UnsupportedFeatures { get; }
        public IReadOnlyList<PdfImage> Images { get; }
        internal IReadOnlyList<IPdfDrawCommand> Commands { get; }
        internal PdfPage(PdfPageInfo info, List<IPdfDrawCommand> commands, HashSet<string> unsupported, List<PdfImage> images)
        { Info = info; Commands = commands.AsReadOnly(); UnsupportedFeatures = Array.AsReadOnly(unsupported.Order(StringComparer.Ordinal).ToArray()); Images = images.AsReadOnly(); }
    }
}
