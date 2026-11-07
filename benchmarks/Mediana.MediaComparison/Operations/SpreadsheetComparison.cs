// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Documents.Spreadsheets;
using Mediana.Documents.Xlsx;

namespace Mediana.OperationComparison
{
    internal static class SpreadsheetComparison
    {
        internal static async Task RunAsync(string input, string destination)
        {
            byte[] encoded = await File.ReadAllBytesAsync(input);
            using SpreadsheetWorkbook workbook = XlsxReader.Open(encoded);
            await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536);
            byte[] number = new byte[sizeof(double)];
            long count = 0;
            foreach (SpreadsheetSheet sheet in workbook.Sheets)
            {
                foreach (SpreadsheetRow row in workbook.ReadRows(sheet.Index))
                {
                    foreach (SpreadsheetCell cell in row.Cells)
                    {
                        if (cell.Value.Kind != SpreadsheetValueKind.Number)
                        {
                            throw new InvalidDataException("Expected the numeric comparison workbook.");
                        }
                        BinaryPrimitives.WriteDoubleLittleEndian(number, cell.Value.Number);
                        output.Write(number);
                        count++;
                    }
                }
            }
            if (count == 0) { throw new InvalidDataException("Empty workbook."); }
            await Console.Out.WriteLineAsync($"cells={count}");
        }
    }
}
