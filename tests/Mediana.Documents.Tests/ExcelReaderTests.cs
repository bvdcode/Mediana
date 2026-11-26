// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Documents.Excel;
using Mediana.Documents.Spreadsheets;
using Mediana.Documents.Xlsx;
using Mediana.Tests;
using Xunit;

namespace Mediana.Documents.Tests
{
    public class ExcelReaderTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void ReadsFirstWorksheetThroughTheSameStreamApi(bool xlsx, bool seekable)
        {
            byte[] bytes = xlsx ? XlsxWorkbookTestData.Create(badSecond: true) : XlsWorkbookTestData.Create();
            using MemoryStream storage = new(bytes);
            if (seekable) { storage.Position = storage.Length; }
            using CountingSourceStream source = new(storage, seekable, maximumRead: 3);
            using (SpreadsheetWorkbook book = ExcelReader.Open(source))
            {
                SpreadsheetRow row = book.ReadRows(book.Sheets[0].Index).First();
                Assert.Equal(xlsx ? "Привет\t世界_x0041_" : "Привет 世界 😀", row.Cells[0].Value.Text);
                Assert.True(row.Cells[1].Value.Boolean);
                Assert.Equal(4, row.Cells[4].Value.Number);
                Assert.NotNull(row.Cells[4].Formula);
            }
            Assert.False(source.Disposed);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ChecksContainerTypeAndLeavesStreamOpenOnFailure(bool seekable)
        {
            using CountingSourceStream unknown = new(new MemoryStream(new byte[65536]), seekable);
            Assert.Throws<InvalidDataException>(() => ExcelReader.Open(unknown));
            Assert.InRange(unknown.BytesRead, 1, 8);
            Assert.False(unknown.Disposed);

            using CountingSourceStream presentation = new(new MemoryStream(PresentationTestData.Create()), seekable);
            Assert.Throws<NotSupportedException>(() => ExcelReader.Open(presentation));
            Assert.False(presentation.Disposed);

            using CountingSourceStream truncated = new(new MemoryStream(new byte[] { 0xd0, 0xcf, 0x11 }), seekable);
            Assert.Throws<InvalidDataException>(() => ExcelReader.Open(truncated));

            using CountingSourceStream cancelled = new(new MemoryStream(XlsWorkbookTestData.Create()), seekable);
            Assert.Throws<OperationCanceledException>(() => ExcelReader.Open(cancelled, cancellationToken: new(true)));
            Assert.Equal(0, cancelled.BytesRead);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EnforcesXlsAndBothXlsxInputLimits(bool seekable)
        {
            byte[] xls = XlsWorkbookTestData.Create();
            using CountingSourceStream binary = new(new MemoryStream(xls), seekable);
            Assert.Throws<InvalidDataException>(() => ExcelReader.Open(binary, xlsOptions: new() { MaximumInputBytes = xls.Length - 1 }));

            byte[] xlsx = XlsxWorkbookTestData.Create();
            foreach (XlsxReaderOptions options in new XlsxReaderOptions[]
            {
                new() { MaximumInputBytes = xlsx.Length - 1 },
                new() { Package = new() { MaximumInputBytes = xlsx.Length - 1 } }
            })
            {
                using CountingSourceStream input = new(new MemoryStream(xlsx), seekable);
                Assert.Throws<InvalidDataException>(() => ExcelReader.Open(input, xlsxOptions: options));
            }
            using CountingSourceStream exact = new(new MemoryStream(xlsx), seekable);
            using SpreadsheetWorkbook book = ExcelReader.Open(exact, xlsxOptions: new() { MaximumInputBytes = xlsx.Length });
            Assert.True(book.ReadRows(0).First().Cells[1].Value.Boolean);
        }

        [Fact]
        public async Task FirstWorksheetSkipsUnrelatedZipPayload()
        {
            using MemoryStream storage = new();
            await storage.WriteAsync(XlsxWorkbookTestData.Create(badSecond: true));
            using (ZipArchive zip = new(storage, ZipArchiveMode.Update, leaveOpen: true))
            {
                await using Stream unused = await zip.CreateEntry("xl/media/unused.bin", CompressionLevel.NoCompression).OpenAsync();
                await unused.WriteAsync(new byte[1024 * 1024]);
            }
            using CountingSourceStream input = new(storage);
            using SpreadsheetWorkbook book = ExcelReader.Open(input);
            Assert.Equal(2, book.ReadRows(0).Count());
            Assert.InRange(input.BytesRead, 1, 128 * 1024);
        }
    }
}
