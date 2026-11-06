// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
namespace Mediana.Documents.Presentation
{
    public static class PresentationReader
    {
        public const string DocumentContentType = "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml";
        /// <summary>Opens a presentation borrowing its input until disposal. Slide content is read on demand.</summary>
        public static PresentationDocument Open(ReadOnlyMemory<byte> input, PresentationReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new();
            OpenXmlPackage package = OpenXmlPackage.Open(input, options.Package, cancellationToken);
            bool completed = false;
            try
            {
                PresentationDocument document = new(package, package.MainPart(cancellationToken), options, true, cancellationToken);
                completed = true;
                return document;
            }
            finally { if (!completed) { package.Dispose(); } }
        }
    }
}
