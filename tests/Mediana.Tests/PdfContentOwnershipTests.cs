// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Pdf;

namespace Mediana.Tests
{
    [Collection("ImageAllocation")]
    public class PdfContentOwnershipTests
    {
        [Fact]
        public async Task ContentStreamsShareOperandsStateAndExactDecodedBudget() =>
            await PdfContentOwnershipTestInput.VerifyAsync();

        [Fact]
        public async Task ContentInterpretationDoesNotDuplicateCompletePayloads() =>
            await PdfContentOwnershipTestInput.VerifyAllocationAsync();

        [Fact]
        public async Task PageContentLimitRejectsRepeatedReferencesAndOneByteExcess()
        {
            byte[] painting = PdfContentOwnershipTestInput.Painting;
            using PdfDocument single = await PdfReader.ReadAsync(new MemoryStream(PdfContentOwnershipTestInput.Create([painting])),
                new() { MaximumDecodedBytes = painting.Length - 1 });
            Assert.Throws<InvalidDataException>(() => single.GetPage(1));
            List<byte[]> objects =
            [
                PdfTestDocument.Bytes("<< /Type /Catalog /Pages 2 0 R >>"),
                PdfTestDocument.Bytes("<< /Type /Pages /Count 1 /Kids [3 0 R] >>"),
                PdfTestDocument.Bytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Contents [4 0 R 4 0 R] >>"),
                PdfTestDocument.Stream(painting)
            ];
            using PdfDocument array = await PdfReader.ReadAsync(new MemoryStream(PdfTestDocument.Write(objects)),
                new() { MaximumDecodedBytes = painting.Length * 2 - 1 });
            Assert.Throws<InvalidDataException>(() => array.GetPage(1));
        }

        [Fact]
        public async Task CancellationAndInvalidContentReferencesDoNotBreakRepeatedReads()
        {
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(PdfContentOwnershipTestInput.Create([PdfContentOwnershipTestInput.Painting])));
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync();
            Assert.Throws<OperationCanceledException>(() => document.GetPage(1, cancellation.Token));
            Assert.Empty(document.GetPage(1).UnsupportedFeatures);
            byte[] invalid = PdfContentOwnershipTestInput.Create([PdfTestDocument.Bytes("q"), PdfTestDocument.Bytes("Q")]);
            string source = System.Text.Encoding.Latin1.GetString(invalid).Replace("[4 0 R 5 0 R]", "[4 0 R 2 0 R]", StringComparison.Ordinal);
            using PdfDocument broken = await PdfReader.ReadAsync(new MemoryStream(PdfTestDocument.Bytes(source)));
            Assert.Throws<InvalidDataException>(() => broken.GetPage(1));
            Assert.Throws<InvalidDataException>(() => broken.GetPage(1));
        }
    }
}
