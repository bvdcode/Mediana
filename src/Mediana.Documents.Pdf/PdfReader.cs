// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.Documents.Pdf
{
    /// <summary>Reads owned, bounded PDF data. Caller streams remain open.</summary>
    public static class PdfReader
    {
        public static async Task<PdfDocument> ReadFileAsync(string path, PdfReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            await using FileStream input = File.OpenRead(path);
            return await ReadAsync(input, options, cancellationToken);
        }
        public static async Task<PdfDocument> ReadAsync(Stream input, PdfReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            options ??= new(); Validate(options);
            byte[] data = await BoundedStreamReader.ReadAsync(input, options.MaximumInputBytes, cancellationToken).ConfigureAwait(false);
            return Create(data, options, cancellationToken);
        }
        /// <summary>The caller keeps input valid until the document and its parsing operations are disposed.</summary>
        internal static PdfDocument ReadBorrowed(ReadOnlyMemory<byte> input, PdfReaderOptions? options, CancellationToken cancellationToken)
        {
            options ??= new();
            Validate(options);
            cancellationToken.ThrowIfCancellationRequested();
            if (input.Length > options.MaximumInputBytes)
            {
                throw new InvalidDataException("PDF encoded input exceeds its byte limit.");
            }
            return Create(input, options, cancellationToken);
        }

        private static PdfDocument Create(ReadOnlyMemory<byte> input, PdfReaderOptions options, CancellationToken token)
        {
            ReadOnlyMemory<byte> content = CmsPdfContent.Unwrap(input, options, token, out bool signedContent);
            return new(new PdfStorage(content, options, token), token, signedContent);
        }

        private static void Validate(PdfReaderOptions options)
        {
            if (options.MaximumInputBytes <= 0 || options.MaximumDecodedBytes <= 0 || options.MaximumObjects <= 0 || options.MaximumValues <= 0 || options.MaximumDepth is < 1 or > 256 || options.MaximumPages <= 0 || options.MaximumOperations <= 0 || options.MaximumImagePixels <= 0 || options.MaximumImageBytes <= 0)
            { throw new ArgumentOutOfRangeException(nameof(options)); }
        }
    }
}
