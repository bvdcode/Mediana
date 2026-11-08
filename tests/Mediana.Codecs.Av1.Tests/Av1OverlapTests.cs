// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1OverlapTests
    {
        [Theory]
        [InlineData("obmcDefault", 13, 1956, 0, 0)]
        [InlineData("obmcRegions", 8, 876, 0, 0)]
        [InlineData("obmcOdd", 8, 660, 0, 0)]
        [InlineData("warpDefault", 13, 1824, 100, 0)]
        [InlineData("warpRegions", 8, 948, 32, 0)]
        [InlineData("warpOdd", 8, 636, 24, 0)]
        [InlineData("ordinary", 13, 1588, 92, 188)]
        [InlineData("ordinaryRegions", 8, 784, 8, 72)]
        [InlineData("ordinaryOdd", 8, 656, 20, 140)]
        public void EveryCodedFrameMatchesIndependentMotionModesIncludingHiddenFrames(string name, int codedFrames, int expectedOverlap, int expectedWarped, int expectedInterIntra)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name + ".obu"));
            using GZipStream source = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", name + ".obmc.gz")), CompressionMode.Decompress);
            using BinaryReader reader = new(source);
            Assert.Equal("AV1OBMC1"u8.ToArray(), reader.ReadBytes(8));
            Assert.Equal(codedFrames, reader.ReadInt32());
            MediaBufferAllocator allocator = new(1024 * 1024);
            int count = 0;
            int overlap = 0;
            int warped = 0;
            int interIntra = 0;
            using (Av1ReferencePictures references = new())
            {
                Av1SequenceHeader sequence = default;
                ushort[] probabilities = new ushort[Av1DefaultCdfs.Count];
                Av1FrameUnitReader units = new(encoded);
                while (units.TryRead(out ReadOnlySpan<byte> unit, default))
                {
                    ReadOnlySpan<byte> frame = default;
                    Av1ObuReader obus = new(unit);
                    while (obus.TryRead(out int type, out ReadOnlySpan<byte> payload))
                    {
                        if (type == 1) { sequence = Av1SequenceHeader.Parse(payload); }
                        if (type == 6) { frame = payload; }
                        if (type == 3)
                        {
                            Av1BitReader bits = new(payload);
                            Assert.True(bits.Flag());
                            Assert.Equal(0, sequence.Tools.FrameIdBits);
                            using Av1Picture displayed = references.Show(bits.Read(3));
                        }
                    }
                    if (frame.IsEmpty) { continue; }
                    Av1FrameHeader header = Av1FrameHeader.Parse(frame, sequence, references);
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
                    tile.Decode();
                    Assert.Equal(geometry.MiRows, reader.ReadUInt16());
                    Assert.Equal(geometry.MiColumns, reader.ReadUInt16());
                    foreach (Av1MotionBlock actual in blocks)
                    {
                        _ = reader.ReadByte();
                        int first = reader.ReadSByte();
                        int rawSecond = reader.ReadSByte();
                        int second = Math.Max(0, rawSecond);
                        int mode = reader.ReadByte();
                        Assert.Equal(first, actual.Reference);
                        Assert.Equal(second, actual.SecondReference);
                        Assert.Equal(first > 0 && rawSecond == 0, actual.InterIntra);
                        if (actual.InterIntra) { interIntra++; }
                        if (first == 0) { continue; }
                        Assert.Equal(mode, actual.MotionMode);
                        if (mode == 1) { overlap++; }
                        if (mode == 2) { warped++; }
                    }
                    if (header.DisableFrameEndUpdate) { Av1TileProbabilities.Initialize(probabilities, header.Quantization.BaseIndex, initial); }
                    using Av1Picture picture = new(header, pixels.Publish());
                    int motionLength = Av1MotionField.StoredLength(geometry, header);
                    using WritableMediaBuffer? motion = motionLength == 0 ? null : allocator.Allocate(motionLength);
                    if (motion is not null) { Av1MotionField.Store(geometry, header, references, blocks, motion.Span); }
                    using MediaBuffer? frozen = motion?.Publish();
                    references.Save(picture, probabilities, allocator, frozen);
                    count++;
                }
            }
            Assert.Equal(codedFrames, count);
            Assert.Equal(expectedOverlap, overlap);
            Assert.Equal(expectedWarped, warped);
            Assert.Equal(expectedInterIntra, interIntra);
            Assert.Equal(-1, source.ReadByte());
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void PredictionDestinationBorrowsOnlyItsLocalRegion()
        {
            byte[] pixels = new byte[12];
            Av1PredictionDestination destination = new(pixels.AsSpan(2, 8), 4, 10, 20);
            destination.Row(11, 21, 2).Fill(7);
            Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0, 0, 7, 7, 0, 0, 0 }, pixels);
        }
    }
}
