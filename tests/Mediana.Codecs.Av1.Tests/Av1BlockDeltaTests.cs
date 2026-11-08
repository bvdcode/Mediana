// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1BlockDeltaTests
    {
        [Theory]
        [InlineData(0x00, false, 0, false, 0, false, 7)]
        [InlineData(0x80, true, 0, false, 0, false, 4)]
        [InlineData(0xE0, true, 3, false, 0, false, 4)]
        [InlineData(0xD6, true, 2, true, 1, true, 1)]
        [InlineData(0xBC, true, 1, true, 3, false, 1)]
        public void HeaderConsumesOnlyDeclaredDeltaParameters(byte value, bool present, int qShift, bool filter, int filterShift, bool multi, int remaining)
        {
            Av1BitReader reader = new(new[] { value });
            Assert.Equal(new(present, qShift, filter, filterShift, multi), Av1BlockDeltaParameters.Read(ref reader, 100));
            Assert.Equal(remaining, reader.RemainingBits);
            Av1BitReader lossless = new([]);
            Assert.Equal(default, Av1BlockDeltaParameters.Read(ref lossless, 0));
        }

        [Theory]
        [InlineData(31, 1, 1, 34)]
        [InlineData(63, 63, -1, 61)]
        [InlineData(0, -63, 1, 1)]
        [InlineData(1, -63, -1, 0)]
        public void FilterClipsBeforeReferenceScalingAndAfterFinalDelta(int level, sbyte delta, sbyte referenceDelta, int expected)
        {
            Av1LoopFilterDeltas references = default;
            references[0] = referenceDelta;
            Av1LoopFilterParameters parameters = new(level, level, level, level, 0, true, references, 0, 0);
            Av1FilterBlock block = default;
            for (int index = 0; index < 4; index++) { block.Deltas[index] = delta; }
            Assert.Equal(expected, parameters.Level(0, 0, block));
            Assert.Equal(expected, parameters.Level(0, 1, block));
            Assert.Equal(expected, parameters.Level(1, 0, block));
            Assert.Equal(expected, parameters.Level(2, 1, block));
        }

        [Theory]
        [InlineData("deltaQRegions", false, 0)]
        [InlineData("deltaLfRegions", true, 48)]
        [InlineData("deltaLfMono", true, 1980)]
        public void EffectiveFilterMetadataMatchesIndependentDecoderAndResetsEachFrame(string name, bool filter, int expectedChanged)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name + ".obu"));
            using GZipStream source = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", name + ".delta.gz")), CompressionMode.Decompress);
            using BinaryReader reader = new(source);
            Assert.Equal("AV1DLTA1"u8.ToArray(), reader.ReadBytes(8));
            Assert.Equal(3, reader.ReadInt32());
            MediaBufferAllocator allocator = new(1024 * 1024);
            using Av1ReferencePictures references = new();
            Av1SequenceHeader sequence = default;
            int changed = 0;
            HashSet<int> quantizers = [];
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
                Assert.True(header.Deltas.QuantizerPresent);
                Assert.Equal(filter, header.Deltas.FilterPresent);
                Assert.Equal(Av1FrameType.Key, header.FrameType);
                Assert.Equal(sequence.MiRows, reader.ReadUInt16());
                Assert.Equal(sequence.MiColumns, reader.ReadUInt16());
                Av1FilterBlock[] blocks = new Av1FilterBlock[sequence.MiRows * sequence.MiColumns];
                using WritableMediaBuffer pixels = allocator.Allocate(sequence.SampleCount);
                Av1TileDecoder tile = new(frame[header.TileOffset..], sequence, header, pixels.Span, new ushort[Av1DefaultCdfs.Count],
                    new byte[sequence.SampleCount / 16], new Av1BlockContext[sequence.ContextCount], default, filterBlocks: blocks,
                    cdefIndices: new sbyte[header.Cdef.IndexCount(sequence)], restorationUnits: new Av1RestorationUnit[header.Restoration.UnitCount(sequence)]);
                tile.Decode();
                foreach (Av1FilterBlock block in blocks)
                {
                    quantizers.Add(reader.ReadByte());
                    sbyte common = reader.ReadSByte();
                    for (int component = 0; component < 4; component++)
                    {
                        sbyte separate = reader.ReadSByte();
                        Assert.Equal(header.Deltas.MultiFilter ? separate : common, block.Deltas[component]);
                    }
                    if (common != 0) { changed++; }
                    Assert.Equal(0, reader.ReadByte());
                    Assert.Equal(0, reader.ReadByte());
                }
            }
            Assert.Equal(expectedChanged, changed);
            Assert.Equal(filter && sequence.Monochrome ? 1 : 2, quantizers.Count);
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(-1, source.ReadByte());
        }
    }
}
