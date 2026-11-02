// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class JpegExifTests
    {
        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data", "JpegExif");
        [Fact]
        public Task AllOrientationsEndiannessAndChromaMatchIndependentPixels() => JpegExifTestInput.VerifyCorpusAsync(Root);
        [Fact]
        public Task MalformedAndConflictingExifFailsWithoutRetainingBuffers() => JpegExifTestInput.VerifyFailuresAsync(Root);
        [Fact]
        public Task ColorDeclarationsStayExplicit() => JpegExifTestInput.VerifyColorAsync(Root);
        [Fact]
        public Task OrientedOutputUsesOneExactAllocationAndComponentsRemainUnoriented() => JpegExifTestInput.VerifyOwnershipAsync(Root);
    }
}
