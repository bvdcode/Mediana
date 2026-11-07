// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Documents.Pdf;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal static class PdfPreviewCommand
    {
        public static Task<int> RenderAsync(string input, CommandOutputFile output, int pageNumber, int maximumWidth,
            ILogger logger, IImageEncoder encoder, CancellationToken cancellationToken = default) =>
            ImagePreviewCommand.RunAsync(new PdfImageDecoder(CommandImageResources.PdfReaderOptions,
                CommandImageResources.PdfRenderOptions with { MaximumWidth = maximumWidth }), input, output, logger, encoder,
                new() { PageIndex = pageNumber - 1 }, cancellationToken);

        public static Task<int> ImageAsync(string input, CommandOutputFile output, int pageNumber, int index,
            ILogger logger, IImageEncoder encoder, CancellationToken cancellationToken = default) =>
            ImagePreviewCommand.RunAsync(new PdfImageDecoder(CommandImageResources.PdfReaderOptions, embeddedImageIndex: index),
                input, output, logger, encoder, new() { PageIndex = pageNumber - 1 }, cancellationToken);
    }
}
