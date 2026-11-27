// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;

namespace Mediana.Tests
{
    public class JpegRestartTests
    {
        [Fact]
        public Task FinalExpectedRestartPreservesFullAndReducedPixelsWithDiagnostics() => JpegRestartTestInput.VerifyRecoveryAsync();

        [Fact]
        public Task WrongPrematureRepeatedOrTruncatedRestartsRemainErrors() => JpegRestartTestInput.VerifyRejectedAsync();

        [Theory]
        [InlineData(65_535)]
        [InlineData(65_536)]
        [InlineData(131_073)]
        public void MetadataScanPreservesStuffingAndRestartsAtWindowEdges(int prefixLength)
        {
            byte[] scan = new byte[prefixLength + 8];
            byte[] markers = [255, 0, 255, 255, 208, 255, 255, 217];
            markers.CopyTo(scan, prefixLength);
            JpegMarkerReader reader = new(scan, 2, default);
            reader.SkipEntropy();
            Assert.Equal(prefixLength + 5, reader.Position);
            Assert.Equal(217, reader.NextMarker());
            scan[^1] = 255;
            Assert.Throws<InvalidDataException>(() => new JpegMarkerReader(scan, 2, default).SkipEntropy());
        }
    }
}
