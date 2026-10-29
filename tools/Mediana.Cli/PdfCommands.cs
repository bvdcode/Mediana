// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using ConsoleAppFramework;

namespace Mediana.Cli
{
    internal class PdfCommands : MediaCommands
    {
        /// <summary>Render a supported PDF page to PNG at up to 96 DPI.</summary>
        /// <param name="input">Existing PDF file.</param>
        /// <param name="output">New PNG file.</param>
        /// <param name="page">One-based page number.</param>
        /// <param name="width">Maximum preview width.</param>
        public Task<int> Render([Argument] string input, [Argument] string output, int page = 1, int width = 1200,
            CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(page);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            return ExecuteAsync(input, output, () => PdfPreviewCommand.RenderAsync(input, output, page, width, cancellationToken));
        }

        /// <summary>Extract one decoded PDF image to PNG.</summary>
        /// <param name="input">Existing PDF file.</param>
        /// <param name="output">New PNG file.</param>
        /// <param name="page">One-based page number.</param>
        /// <param name="index">Zero-based decoded image index on the page.</param>
        public Task<int> Image([Argument] string input, [Argument] string output, int page = 1, int index = 0,
            CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(page);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            return ExecuteAsync(input, output, () => PdfPreviewCommand.ImageAsync(input, output, page, index, cancellationToken));
        }
    }
}
