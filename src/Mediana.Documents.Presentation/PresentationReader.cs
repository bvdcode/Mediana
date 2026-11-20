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
            return Open(package, options, cancellationToken);
        }

        /// <summary>Opens a presentation from a readable seekable stream, reading only requested parts.
        /// By default the caller retains the stream and must keep it unchanged until the presentation is disposed.</summary>
        public static PresentationDocument Open(Stream input, PresentationReaderOptions? options = null, bool leaveOpen = true,
            CancellationToken cancellationToken = default)
        {
            options ??= new();
            OpenXmlPackage package = OpenXmlPackage.Open(input, options.Package, leaveOpen, cancellationToken);
            return Open(package, options, cancellationToken);
        }

        private static PresentationDocument Open(OpenXmlPackage package, PresentationReaderOptions options, CancellationToken cancellationToken)
        {
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
