// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1ChromaPaletteTests
    {
        [Theory]
        [InlineData("defaultKey160", 1, 7)]
        [InlineData("chromaPaletteOdd", 3, 6)]
        [InlineData("chromaPaletteRegions", 3, 6)]
        public void OrdinaryGraphicPalettesMatchIndependentCacheValues(string name, int pictures, int expectedSizes)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name + ".obu"));
            using GZipStream source = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", name + ".palette.gz")), CompressionMode.Decompress);
            using BinaryReader reader = new(source);
            Assert.Equal("AV1PAL01"u8.ToArray(), reader.ReadBytes(8));
            Assert.Equal(pictures, reader.ReadInt32());
            MediaBufferAllocator allocator = new(1024 * 1024);
            using Av1ReferencePictures references = new();
            Av1SequenceHeader sequence = default;
            HashSet<int> sizes = [];
            foreach (ReadOnlyMemory<byte> unit in Av1SequencePictureTests.AccessUnits(encoded))
            {
                ReadOnlySpan<byte> frame = default;
                Av1ObuReader obus = new(unit.Span);
                while (obus.TryRead(out int type, out ReadOnlySpan<byte> payload))
                {
                    if (type == 1) { sequence = Av1SequenceHeader.Parse(payload); }
                    if (type == 6) { frame = payload; }
                }
                Av1FrameHeader header = Av1FrameHeader.Parse(frame, sequence, references);
                Assert.Equal(Av1FrameType.Key, header.FrameType);
                Assert.Equal(sequence.MiRows, reader.ReadUInt16());
                Assert.Equal(sequence.MiColumns, reader.ReadUInt16());
                Av1BlockContext[] contexts = new Av1BlockContext[sequence.ContextCount];
                using WritableMediaBuffer pixels = allocator.Allocate(sequence.SampleCount);
                Av1TileDecoder tile = new(frame[header.TileOffset..], sequence, header, pixels.Span, new ushort[Av1DefaultCdfs.Count],
                    new byte[sequence.SampleCount / 16], contexts, default, filterBlocks: new Av1FilterBlock[sequence.MiRows * sequence.MiColumns],
                    cdefIndices: new sbyte[header.Cdef.IndexCount(sequence)], restorationUnits: new Av1RestorationUnit[header.Restoration.UnitCount(sequence)]);
                tile.Decode();
                for (int row = 0; row < sequence.MiRows; row++)
                {
                    for (int column = 0; column < sequence.MiColumns; column++)
                    {
                        int luma = reader.ReadByte();
                        int count = reader.ReadByte();
                        byte[] u = reader.ReadBytes(8);
                        _ = reader.ReadBytes(8);
                        if (count > 0) { sizes.Add(count); }
                        if (row != sequence.MiRows - 1) { continue; }
                        Assert.Equal(luma, contexts[column].PaletteCount);
                        Assert.Equal(count, contexts[column].UvPaletteCount);
                        for (int index = 0; index < count; index++) { Assert.Equal(u[index], contexts[column].UvPalette[index]); }
                    }
                }
            }
            Assert.Equal(expectedSizes, sizes.Count);
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(-1, source.ReadByte());
        }
    }
}
