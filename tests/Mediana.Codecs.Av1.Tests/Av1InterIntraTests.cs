// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1InterIntraTests
    {
        [Fact]
        public void SmoothMasksMatchIndependentReferenceAcrossAllBlockShapes()
        {
            using GZipStream source = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "inter-intra-masks.gz")), CompressionMode.Decompress);
            using BinaryReader reader = new(source);
            Assert.Equal("AV1IIM01"u8.ToArray(), reader.ReadBytes(8));
            int count = reader.ReadInt32();
            Assert.Equal(88, count);
            for (int index = 0; index < count; index++)
            {
                int width = reader.ReadByte();
                int height = reader.ReadByte();
                int mode = reader.ReadByte();
                for (int row = 0; row < height; row++)
                {
                    for (int column = 0; column < width; column++)
                    {
                        Assert.Equal(reader.ReadByte(), Av1InterIntraMask.Weight(mode, width, height, row, column));
                    }
                }
            }
            Assert.Equal(-1, source.ReadByte());
        }
    }
}
