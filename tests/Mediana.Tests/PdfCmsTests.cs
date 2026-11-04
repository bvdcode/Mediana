// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Formats.Asn1;
using Mediana.Documents.Pdf;

namespace Mediana.Tests
{
    public class PdfCmsTests
    {
        [Fact]
        public Task SignedDataSupportsPrimitiveAndConstructedOctetsAndPublishesDiagnostics() => PdfCmsTestInput.VerifyAsync();

        [Fact]
        public async Task TruncationAndTrailingDataAreRejected()
        {
            byte[] input = PdfCmsTestInput.Create(PdfCmsTestInput.Pdf());
            for (int length = 0; length < input.Length; length++)
            {
                Exception? error = await Record.ExceptionAsync(async () =>
                {
                    using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(input, 0, length));
                });
                Assert.True(error is AsnContentException or InvalidDataException, $"CMS prefix {length} was not rejected structurally.");
            }
            await Assert.ThrowsAsync<AsnContentException>(async () =>
            {
                using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream([.. input, 0]));
            });
        }

        [Fact]
        public async Task ContentTypeAndDetachedOrNonPdfContentAreRejected()
        {
            await Assert.ThrowsAsync<NotSupportedException>(async () =>
            {
                using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(PdfCmsTestInput.Create(null)));
            });
            await Assert.ThrowsAsync<NotSupportedException>(async () =>
            {
                using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(PdfCmsTestInput.Create(PdfCmsTestInput.Pdf(), contentType: "1.2.840.113549.1.7.3")));
            });
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(PdfCmsTestInput.Create("document"u8.ToArray())));
            });
            PdfImageDecoder decoder = new();
            Assert.False(decoder.CanDecode("prefix %PDF-1.7"u8));
            Assert.False(decoder.CanDecode(new byte[] { 0x30, 0x09, 0x06, 0x09, 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x07, 0x03 }));
        }

        [Fact]
        public async Task EnvelopeByteAndEntryBudgetsAndCancellationRemainEffective()
        {
            byte[] input = PdfCmsTestInput.Create(PdfCmsTestInput.Pdf(), certificates: 10);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(input), new() { MaximumObjects = 8 });
            });
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(input), new() { MaximumInputBytes = input.Length - 1 });
            });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(input), cancellationToken: new CancellationToken(true));
            });
        }
    }
}
