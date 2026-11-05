// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Formats.Metadata;

namespace Mediana.Tests
{
    public class ExifMetadataTests
    {
        [Fact]
        public void BothByteOrdersRetainNativeTextFractionsGpsAndBinaryOwnership() => ExifMetadataTestInput.Verify();

        [Fact]
        public void TruncationCyclesValueExtentsAndCancellationFailExplicitly()
        {
            byte[] valid = ExifMetadataTestInput.Create(true);
            for (int length = 0; length < valid.Length; length++)
            {
                Assert.Throws<InvalidDataException>(() => ExifMetadataReader.Read(valid.AsMemory(0, length)));
            }
            byte[] cyclic = valid.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(cyclic.AsSpan(46), 8);
            Assert.Throws<InvalidDataException>(() => ExifMetadataReader.Read(cyclic));
            byte[] outside = valid.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(outside.AsSpan(18), uint.MaxValue);
            Assert.Throws<InvalidDataException>(() => ExifMetadataReader.Read(outside));
            Assert.Throws<OperationCanceledException>(() => ExifMetadataReader.Read(valid, new(true)));
        }
    }
}
