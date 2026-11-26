// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using Mediana.Documents.Spreadsheets;
using Mediana.Documents.Xls;
using Mediana.Documents.Xlsx;
using Mediana.IO;

namespace Mediana.Documents.Excel
{
    public static class ExcelReader
    {
        /// <summary>Opens a BIFF8 or SpreadsheetML workbook by content. Seekable input contains the
        /// complete file. Forward-only input starts at its current position. The caller owns the stream
        /// and must keep it open and unchanged until the workbook is disposed.</summary>
        public static SpreadsheetWorkbook Open(Stream input, XlsReaderOptions? xlsOptions = null,
            XlsxReaderOptions? xlsxOptions = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (!input.CanRead) { throw new ArgumentException("Workbook input must be readable.", nameof(input)); }
            cancellationToken.ThrowIfCancellationRequested();
            if (input.CanSeek) { input.Position = 0; }
            Span<byte> probe = stackalloc byte[8];
            int count = input.ReadAtLeast(probe, probe.Length, throwOnEndOfStream: false);
            ReadOnlySpan<byte> prefix = probe[..count];
            cancellationToken.ThrowIfCancellationRequested();

            if (OpenXmlPackage.HasZipSignature(prefix))
            {
                xlsxOptions ??= new();
                if (input.CanSeek) { return XlsxReader.Open(input, xlsxOptions, cancellationToken); }

                xlsxOptions.Validate(count);
                long limit = Math.Min(xlsxOptions.MaximumInputBytes, xlsxOptions.Package.MaximumInputBytes);
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
                byte[] bytes = BoundedStreamReader.ReadWithPrefix(input, (int)limit, prefix, cancellationToken);
                return XlsxReader.Open(bytes, xlsxOptions, cancellationToken);
            }
            if (XlsReader.HasCompoundFileSignature(prefix))
            {
                xlsOptions ??= new();
                xlsOptions.Validate(count);
                byte[] bytes = input.CanSeek
                    ? ReadCompoundFile(input, prefix, xlsOptions.MaximumInputBytes, cancellationToken)
                    : BoundedStreamReader.ReadWithPrefix(input, xlsOptions.MaximumInputBytes, prefix, cancellationToken);
                return XlsReader.Open(bytes, xlsOptions, cancellationToken);
            }
            throw new InvalidDataException("The input has no recognized XLS or XLSX container signature.");
        }

        private static byte[] ReadCompoundFile(Stream input, ReadOnlySpan<byte> prefix, int limit, CancellationToken token)
        {
            long length = input.Length;
            if (length < prefix.Length || length > limit) { throw new InvalidDataException("Invalid XLS input length or MaximumInputBytes exceeded."); }
            byte[] result = new byte[(int)length];
            prefix.CopyTo(result);
            int offset = prefix.Length;
            while (offset < result.Length)
            {
                token.ThrowIfCancellationRequested();
                int count = input.Read(result.AsSpan(offset));
                if (count == 0) { throw new EndOfStreamException("Incomplete XLS compound file."); }
                offset += count;
            }
            return result;
        }
    }
}
