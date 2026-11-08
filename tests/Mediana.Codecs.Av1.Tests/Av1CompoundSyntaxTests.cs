// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.IO.Compression;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1CompoundSyntaxTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        [Theory]
        [InlineData("compoundAverage", 24, 0, 0)]
        [InlineData("compoundDistance", 184, 0, 152)]
        [InlineData("compoundFree", 44, 0, 40)]
        [InlineData("compoundBlend", 532, 192, 68)]
        [InlineData("compoundLossless", 576, 192, 8)]
        [InlineData("compoundLookahead", 588, 0, 16)]
        public void EncodedCorpusExercisesDualReferencePrediction(string name, int expectedCompound, int expectedSkip, int expectedDistance)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name + ".obu"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            int compound = 0;
            int skipped = 0;
            int distance = 0;
            int index = 0;
            using FileStream motionFile = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", name + ".motion.gz"));
            using GZipStream expanded = new(motionFile, CompressionMode.Decompress);
            using BinaryReader motionReader = new(expanded);
            Assert.Equal("AV1MOTN2"u8.ToArray(), motionReader.ReadBytes(8));
            Assert.Equal(10, motionReader.ReadInt32());
            using (Av1ReferencePictures references = new())
            {
                Av1SequenceHeader sequence = default;
                ushort[] probabilities = new ushort[Av1DefaultCdfs.Count];
                foreach (ReadOnlyMemory<byte> unit in Av1SequencePictureTests.AccessUnits(encoded))
                {
                    ReadOnlySpan<byte> frame = default;
                    Av1ObuReader obus = new(unit.Span);
                    while (obus.TryRead(out int type, out ReadOnlySpan<byte> payload))
                    {
                        if (type == 1) { sequence = Av1SequenceHeader.Parse(payload); }
                        if (type == 6) { frame = payload; }
                    }
                    Assert.False(frame.IsEmpty);
                    Av1FrameHeader header = Av1FrameHeader.Parse(frame, sequence, references);
                    output.WriteLine($"Frame {index++}: skip mode {header.Inter.SkipMode}, reference motion {header.Inter.ReferenceMotion}; prior compound {compound}, skip {skipped}, distance {distance} cells.");
                    references.Invalidate(header.InvalidReferences);
                    Av1SequenceHeader geometry = header.Geometry;
                    int cells = geometry.MiColumns * geometry.MiRows;
                    Av1MotionBlock[] blocks = new Av1MotionBlock[cells];
                    Av1MotionFieldSample[] field = new Av1MotionFieldSample[header.Inter.ReferenceMotion ? cells / 4 : 0];
                    if (header.Inter.ReferenceMotion) { Av1MotionField.Estimate(geometry, header, references, field, default); }
                    ReadOnlySpan<ushort> initial = header.Inter.PrimaryReference != Av1InterPrediction.NoPrimaryReference && header.FrameType is Av1FrameType.Inter or Av1FrameType.Switch
                        ? MemoryMarshal.Cast<byte, ushort>(references.Get(header.Inter.References[header.Inter.PrimaryReference]).Cdfs.Memory.Span) : default;
                    using WritableMediaBuffer pixels = allocator.Allocate(geometry.SampleCount);
                    Av1TileDecoder tile = new(frame[header.TileOffset..], geometry, header, pixels.Span, probabilities,
                        new byte[geometry.SampleCount / 16], new Av1BlockContext[geometry.ContextCount], default, initial, references, blocks,
                        new Av1FilterBlock[cells], new sbyte[header.Cdef.IndexCount(geometry)],
                        new Av1RestorationUnit[header.Restoration.UnitCount(geometry)], field);
                    try { tile.Decode(); }
                    finally { AssertNativeMotion(motionReader, geometry, blocks, index - 1); }
                    foreach (Av1MotionBlock block in blocks)
                    {
                        if (block.SecondReference == 0) { continue; }
                        compound++;
                        if (block.SkipMode) { skipped++; }
                        if (block.CompoundIndex == 0) { distance++; }
                    }
                    if (header.DisableFrameEndUpdate) { Av1TileProbabilities.Initialize(probabilities, header.Quantization.BaseIndex, initial); }
                    using Av1Picture picture = new(header, pixels.Publish());
                    int motionLength = Av1MotionField.StoredLength(geometry, header);
                    using WritableMediaBuffer? motion = motionLength == 0 ? null : allocator.Allocate(motionLength);
                    if (motion is not null) { Av1MotionField.Store(geometry, header, references, blocks, motion.Span); }
                    using MediaBuffer? frozen = motion?.Publish();
                    references.Save(picture, probabilities, allocator, frozen);
                }
            }
            Assert.Equal(expectedCompound, compound);
            Assert.Equal(expectedSkip, skipped);
            Assert.Equal(expectedDistance, distance);
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(-1, expanded.ReadByte());
        }

        private static void AssertNativeMotion(BinaryReader reader, Av1SequenceHeader sequence, ReadOnlySpan<Av1MotionBlock> blocks, int frame)
        {
            Assert.Equal(sequence.MiRows, reader.ReadUInt16());
            Assert.Equal(sequence.MiColumns, reader.ReadUInt16());
            for (int y = 0; y < sequence.MiRows; y++)
            {
                for (int x = 0; x < sequence.MiColumns; x++)
                {
                    int size = reader.ReadByte();
                    int mode = reader.ReadByte();
                    int first = reader.ReadSByte();
                    int second = Math.Max(0, (int)reader.ReadSByte());
                    int row = reader.ReadInt16();
                    int column = reader.ReadInt16();
                    int secondRow = reader.ReadInt16();
                    int secondColumn = reader.ReadInt16();
                    int referenceIndex = reader.ReadByte();
                    bool skipMode = reader.ReadByte() != 0;
                    int compoundIndex = reader.ReadByte();
                    int compoundGroup = reader.ReadByte();
                    Av1MotionBlock actual = blocks[y * sequence.MiColumns + x];
                    Assert.True(actual.Reference == first && actual.SecondReference == second,
                        $"Frame {frame}, MI {y},{x}: refs {actual.Reference}/{actual.SecondReference}, expected {first}/{second}, size {size}.");
                    if (first == 0) { continue; }
                    Assert.True(actual.Mode == mode && actual.Row == row && actual.Column == column,
                        $"Frame {frame}, MI {y},{x}: mode {actual.Mode}, MV {actual.Row},{actual.Column}; expected mode {mode}, MV {row},{column}, reference-MV index {referenceIndex}.");
                    Assert.Equal(skipMode, actual.SkipMode);
                    if (second == 0) { continue; }
                    Assert.True(actual.SecondRow == secondRow && actual.SecondColumn == secondColumn,
                        $"Frame {frame}, MI {y},{x}: second MV {actual.SecondRow},{actual.SecondColumn}; expected {secondRow},{secondColumn}.");
                    Assert.Equal(compoundIndex, actual.CompoundIndex);
                    Assert.Equal(compoundGroup, actual.CompoundGroup);
                }
            }
        }
    }
}
