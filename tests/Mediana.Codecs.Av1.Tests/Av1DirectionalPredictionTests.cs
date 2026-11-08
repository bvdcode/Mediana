// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1DirectionalPredictionTests
    {
        [Fact]
        public void NativeFilteredAndUpsampledEdgesMatchEveryDirectionAndGeometry()
        {
            using FileStream file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "directional.bin.gz"));
            using GZipStream expanded = new(file, CompressionMode.Decompress);
            using BinaryReader reader = new(expanded);
            Assert.Equal("AV1DIR01"u8.ToArray(), reader.ReadBytes(8));
            Assert.Equal(4256, reader.ReadInt32());
            for (int test = 0; test < 4256; test++)
            {
                int width = reader.ReadByte();
                int height = reader.ReadByte();
                int angle = reader.ReadByte();
                int filterType = reader.ReadByte();
                int flags = reader.ReadByte();
                int visibleWidth = reader.ReadByte();
                int visibleHeight = reader.ReadByte();
                int edgeCount = width + height;
                byte[] above = new byte[edgeCount * 2 + 2];
                byte[] left = new byte[edgeCount * 2 + 2];
                reader.ReadBytes(edgeCount + 2).CopyTo(above, 0);
                reader.ReadBytes(edgeCount + 2).CopyTo(left, 0);
                byte[] expected = reader.ReadBytes(width * height);
                Assert.Equal(width * height, expected.Length);
                byte[] actual = new byte[width * height];
                actual.AsSpan().Fill(237);
                Av1IntraEdges edges = new(above, left, visibleWidth, visibleHeight, (flags & 1) != 0, (flags & 2) != 0, filterType == 1);
                Av1DirectionalPrediction.Predict(actual, width, new(width, height), edges, angle, true);
                for (int y = 0; y < height; y++)
                {
                    if (y < visibleHeight)
                    {
                        Assert.True(actual.AsSpan(y * width, visibleWidth).SequenceEqual(expected.AsSpan(y * width, visibleWidth)),
                            $"Directional vector {test}, {width}x{height}, angle {angle}, type {filterType}, flags {flags}, row {y} differs.");
                        Assert.Equal(-1, actual.AsSpan(y * width + visibleWidth, width - visibleWidth).IndexOfAnyExcept((byte)237));
                    }
                    else
                    {
                        Assert.Equal(-1, actual.AsSpan(y * width, width).IndexOfAnyExcept((byte)237));
                    }
                }
            }
            Assert.Equal(-1, expanded.ReadByte());
        }
    }
}
