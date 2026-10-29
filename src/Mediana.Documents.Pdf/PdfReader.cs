// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

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
            using MemoryStream output = new();
            byte[] buffer = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
            {
                if (output.Length + count > options.MaximumInputBytes) { throw new InvalidDataException("PDF encoded input exceeds its byte limit."); }
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
            return new(new PdfStorage(output.ToArray(), options, cancellationToken), cancellationToken);
        }
        private static void Validate(PdfReaderOptions options)
        {
            if (options.MaximumInputBytes <= 0 || options.MaximumDecodedBytes <= 0 || options.MaximumObjects <= 0 || options.MaximumValues <= 0 || options.MaximumDepth is < 1 or > 256 || options.MaximumPages <= 0 || options.MaximumOperations <= 0 || options.MaximumImagePixels <= 0 || options.MaximumImageBytes <= 0)
            { throw new ArgumentOutOfRangeException(nameof(options)); }
        }
    }
}
