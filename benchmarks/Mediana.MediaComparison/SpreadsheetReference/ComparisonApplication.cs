// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Buffers;
using ExcelDataReader;

namespace Mediana.SpreadsheetReference
{
    internal static class ComparisonApplication
    {
        internal static async Task RunAsync(string[] arguments)
        {
            if (arguments.Length != 2) { throw new ArgumentException("Expected input workbook and new binary output."); }
            byte[] encoded = await File.ReadAllBytesAsync(arguments[0]);
            using MemoryStream input = new(encoded, writable: false);
            using IExcelDataReader reader = ExcelReaderFactory.CreateOpenXmlReader(input);
            await using FileStream output = new(arguments[1], FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, FileOptions.Asynchronous);
            ArrayBufferWriter<byte> buffer = new();
            long count = 0;
            do
            {
                while (reader.Read())
                {
                    buffer.Clear();
                    for (int column = 0; column < reader.FieldCount; column++)
                    {
                        if (reader.GetValue(column) is not double value)
                        {
                            throw new InvalidDataException("Expected the numeric comparison workbook.");
                        }
                        BinaryPrimitives.WriteDoubleLittleEndian(buffer.GetSpan(sizeof(double)), value);
                        buffer.Advance(sizeof(double));
                        count++;
                    }
                    await output.WriteAsync(buffer.WrittenMemory);
                }
            } while (reader.NextResult());
            if (count == 0) { throw new InvalidDataException("Empty workbook."); }
            await Console.Out.WriteLineAsync($"cells={count}");
        }
    }
}
