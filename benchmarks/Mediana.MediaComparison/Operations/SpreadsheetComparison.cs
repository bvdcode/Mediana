// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Buffers;
using Mediana.Documents.Spreadsheets;
using Mediana.Documents.Xlsx;

namespace Mediana.OperationComparison
{
    internal static class SpreadsheetComparison
    {
        internal static async Task RunAsync(string input, string destination)
        {
            byte[] encoded = await File.ReadAllBytesAsync(input);
            XlsxReaderOptions options = new() { Package = new() { MaximumXmlElements = 2_000_000 } };
            using SpreadsheetWorkbook workbook = XlsxReader.Open(encoded, options);
            await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, FileOptions.Asynchronous);
            ArrayBufferWriter<byte> buffer = new();
            long count = 0;
            foreach (SpreadsheetSheet sheet in workbook.Sheets)
            {
                foreach (SpreadsheetRow row in workbook.ReadRows(sheet.Index))
                {
                    buffer.Clear();
                    foreach (SpreadsheetCell cell in row.Cells)
                    {
                        if (cell.Value.Kind != SpreadsheetValueKind.Number)
                        {
                            throw new InvalidDataException("Expected the numeric comparison workbook.");
                        }
                        BinaryPrimitives.WriteDoubleLittleEndian(buffer.GetSpan(sizeof(double)), cell.Value.Number);
                        buffer.Advance(sizeof(double));
                        count++;
                    }
                    await output.WriteAsync(buffer.WrittenMemory);
                }
            }
            if (count == 0) { throw new InvalidDataException("Empty workbook."); }
            await Console.Out.WriteLineAsync($"cells={count}");
        }
    }
}
