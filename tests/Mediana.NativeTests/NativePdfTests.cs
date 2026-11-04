// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativePdfTests
    {
        internal static async Task RunAsync()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            await PdfType3TestInput.VerifyPaintingAsync();
            await PdfScaledImageTestInput.VerifyBudgetsAsync(Path.Combine(root, "Jpeg"));
            await PdfScaledImageTestInput.VerifyMultiplePlacementsAsync(Path.Combine(root, "Jpeg"));
            await PdfScaledImageTestInput.VerifyOwnershipAsync();
            await PdfScaledImageTestInput.VerifyLargeScanAsync();
            await PdfInvisibleTextTestInput.VerifyAsync();
            await PdfPageGroupTestInput.VerifyAsync();
            FontGlyphTestInput.VerifyRejectedAllocation();
            FontGlyphTestInput.VerifyTruncatedGlyphs();
            FontGlyphTestInput.VerifyValidOutlines();
            FontGlyphTestInput.VerifyEmptyOutlines();
            FontGlyphTestInput.VerifyCompositeInstructions();
            await PdfReferenceTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Pdf"));
            await PdfJpegTestInput.VerifyCorpusAsync(Path.Combine(AppContext.BaseDirectory, "Data", "PdfJpeg"));
            await PdfJpegTestInput.VerifyInvalidAsync();
            await PdfJpegTestInput.VerifyLimitsAndReuseAsync();
            await PdfJpegTestInput.VerifyAllocationAsync(Path.Combine(AppContext.BaseDirectory, "Data", "PdfJpeg"));
            await PdfJpegTestInput.VerifyMaskReferencesAsync();
            await PdfContentOwnershipTestInput.VerifyAsync();
            await PdfContentOwnershipTestInput.VerifyAllocationAsync();
            await PdfStreamOwnershipTestInput.VerifyAsync();
            await PdfStreamOwnershipTestInput.VerifyAllocationAsync();
            await PdfStreamOwnershipTestInput.VerifyFlateAllocationAsync();
            await PdfByteFilterTestInput.VerifyAsciiHexAsync();
            await PdfByteFilterTestInput.VerifyHexBlocksAsync();
            await PdfByteFilterTestInput.VerifyAscii85Async();
            await PdfByteFilterTestInput.VerifyRunLengthAsync();
            await PdfByteFilterTestInput.VerifyInvalidAsync();
            foreach (string filter in new[] { "ASCIIHexDecode", "ASCII85Decode", "RunLengthDecode" })
            {
                await PdfByteFilterTestInput.VerifyLimitsAsync(filter);
                await PdfByteFilterTestInput.VerifyAllocationAsync(filter);
                await PdfByteFilterTestInput.VerifyRejectedAllocationAsync(filter, false);
                await PdfByteFilterTestInput.VerifyRejectedAllocationAsync(filter, true);
            }
            Console.WriteLine("PASS PDF: detached pages, vector order, clipping, transparency and bounded exact byte filters.");
        }
    }
}
