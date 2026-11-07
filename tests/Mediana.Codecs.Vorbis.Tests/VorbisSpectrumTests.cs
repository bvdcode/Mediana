// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Vorbis.Tests
{
    public class VorbisSpectrumTests
    {
        [Fact]
        public void EncodedSpectraMatchIndependentReference() =>
            Assert.Equal(1183, VorbisSpectrumTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void FloorTypesResidueTypesAndTruncatedPacketsMatchIndependentReference() =>
            Assert.Equal(192, VorbisSpectrumTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data"), "synthetic-spectrum.bin.gz"));

        [Fact]
        public void RepeatedSpectralDecodingAllocatesNoWorkingStorage() =>
            Assert.Equal(1183, VorbisSpectrumTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data"), measureAllocations: true));
    }
}
