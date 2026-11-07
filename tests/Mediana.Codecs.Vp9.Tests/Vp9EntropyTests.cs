// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.Vp9;

namespace Mediana.Codecs.Vp9.Tests
{
    public class Vp9EntropyTests
    {
        [Fact]
        public async Task BooleanDecodingMatchesIndependentLibvpxEncoderIncludingExtremeProbabilities()
        {
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "boolean.bin"));
            DecodeBooleanVector(encoded);
            byte[] nonzeroPadding = [.. encoded, 1];
            Assert.Throws<InvalidDataException>(() => DecodeBooleanVector(nonzeroPadding));
            Assert.Throws<InvalidDataException>(() => DecodeBooleanVector(encoded[..^4]));
            Assert.Throws<InvalidDataException>(() => DecodeBooleanVector([]));
            Assert.Throws<InvalidDataException>(() => DecodeBooleanVector([0xff, 0, 0]));
        }

        private static void DecodeBooleanVector(ReadOnlySpan<byte> encoded)
        {
            Vp9BooleanReader bits = new(encoded);
            for (int i = 0; i < 4096; i++)
            {
                int probability = (i * 73 + (i >> 3) * 19) & 255;
                bool expected = (((long)i * 1103515245 + 12345) >> 16 & 1) != 0;
                Assert.Equal(expected, bits.Read(probability));
            }
            bits.Finish();
        }

        [Fact]
        public void AllProbabilityRemappingsStayInsideTheNormativeNonzeroRange()
        {
            for (int probability = 1; probability <= 255; probability++)
            {
                for (int delta = 0; delta < 255; delta++)
                { Assert.InRange(Vp9ProbabilityUpdate.Remap(delta, probability), (byte)1, (byte)255); }
            }
            Assert.Equal(8, Vp9ProbabilityUpdate.Remap(0, 1));
            Assert.Equal(248, Vp9ProbabilityUpdate.Remap(0, 255));
            Assert.Equal(124, Vp9ProbabilityUpdate.Remap(0, 128));
        }

        [Theory]
        [InlineData("main8")]
        [InlineData("main10")]
        [InlineData("high8")]
        [InlineData("high12")]
        [InlineData("lossless")]
        [InlineData("resilient")]
        [InlineData("tiles")]
        [InlineData("altref")]
        public async Task IndependentStreamsHaveCompleteCompressedHeadersWithZeroPadding(string name)
        {
            byte[] ivf = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", name + ".ivf"));
            ValidateCompressedHeaders(ivf);
        }

        private static void ValidateCompressedHeaders(byte[] ivf)
        {
            Vp9HeaderState state = new(); Vp9ProbabilityState probabilities = new(); Range[] ranges = new Range[8];
            int cursor = 32, count = 0;
            while (cursor < ivf.Length)
            {
                int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(ivf.AsSpan(cursor))); cursor += 12;
                ReadOnlyMemory<byte> packet = ivf.AsMemory(cursor, length); cursor += length;
                int frames = Vp9Superframe.Parse(packet.Span, ranges);
                for (int i = 0; i < frames; i++)
                {
                    ReadOnlyMemory<byte> frame = packet[ranges[i]];
                    Vp9FrameHeader header = Vp9FrameHeader.Parse(frame.Span, state);
                    if (!header.ShowExisting)
                    {
                        probabilities.Begin(header);
                        Vp9CompressedHeader compressed = Vp9CompressedHeader.Parse(frame.Span.Slice(header.CompressedOffset, header.CompressedLength), header, probabilities.Working);
                        Assert.InRange(compressed.TransformMode, 0, 4); Assert.InRange(compressed.ReferenceMode, 0, 2);
                        if (header.Lossless) { Assert.Equal(0, compressed.TransformMode); }
                        if (header.RefreshContext) { probabilities.Save(header.Context); }
                        int tileCount = 0;
                        Vp9TileReader tiles = new(frame[(header.CompressedOffset + header.CompressedLength)..], header);
                        while (tiles.MoveNext())
                        {
                            Vp9Tile tile = tiles.Current; Vp9BooleanReader arithmetic = new(tile.Encoded.Span);
                            Assert.InRange(tile.RowStart, 0, header.MiRows); Assert.InRange(tile.RowEnd, tile.RowStart, header.MiRows);
                            Assert.InRange(tile.ColumnStart, 0, header.MiColumns); Assert.InRange(tile.ColumnEnd, tile.ColumnStart, header.MiColumns);
                            tileCount++;
                        }
                        Assert.Equal(1 << (header.TileColumnsLog2 + header.TileRowsLog2), tileCount); count++;
                    }
                    state.Commit(header);
                }
            }
            Assert.True(count > 10);
        }
    }
}
