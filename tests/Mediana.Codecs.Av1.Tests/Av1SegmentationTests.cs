// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1SegmentationTests
    {
        [Theory]
        [InlineData("segmentationCyclic", 8, 1514)]
        [InlineData("segmentationOdd", 8, 440)]
        [InlineData("segmentationFeatures", 11, 144)]
        [InlineData("segmentationMixed", 11, 144)]
        public void EverySegmentSkipReferenceAndFixedTransformMatchesIndependentCodedFrames(string name, int expectedFrames, int expectedNonzero)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name + ".obu"));
            using GZipStream source = Open(name + ".segment.gz");
            using BinaryReader reader = new(source);
            Assert.Equal("AV1SEGM2"u8.ToArray(), reader.ReadBytes(8));
            Assert.Equal(expectedFrames, reader.ReadInt32());
            MediaBufferAllocator allocator = new(1024 * 1024);
            int count = 0;
            int nonzero = 0;
            bool inherited = false;
            bool temporal = false;
            bool reset = false;
            bool mixed = name == "segmentationMixed";
            bool conformance = mixed || name == "segmentationFeatures";
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
                    }
                    if (frame.IsEmpty) { continue; }
                    Av1FrameHeader header = Av1FrameHeader.Parse(frame, sequence, references);
                    references.Invalidate(header.InvalidReferences);
                    Av1SequenceHeader geometry = header.Geometry;
                    int cells = geometry.MiColumns * geometry.MiRows;
                    Av1MotionBlock[] blocks = new Av1MotionBlock[cells];
                    Av1FilterBlock[] filters = new Av1FilterBlock[cells];
                    Av1MotionFieldSample[] field = new Av1MotionFieldSample[header.Inter.ReferenceMotion ? cells / 4 : 0];
                    if (header.Inter.ReferenceMotion) { Av1MotionField.Estimate(geometry, header, references, field, default); }
                    Av1StoredPicture? previous = header.Inter.PrimaryReference == Av1InterPrediction.NoPrimaryReference
                        ? null : references.Get(header.Inter.References[header.Inter.PrimaryReference]);
                    ReadOnlySpan<ushort> initial = previous is null ? default : MemoryMarshal.Cast<byte, ushort>(previous.Cdfs.Memory.Span);
                    ReadOnlySpan<byte> previousMap = previous is not null && previous.Header.Geometry.MiRows == geometry.MiRows && previous.Header.Geometry.MiColumns == geometry.MiColumns && previous.SegmentMap is not null
                        ? previous.SegmentMap.Memory.Span : default;
                    using WritableMediaBuffer pixels = allocator.Allocate(geometry.SampleCount);
                    using WritableMediaBuffer? segments = header.Segmentation.Enabled ? allocator.Allocate(cells) : null;
                    Av1TileDecoder tile = new(frame[header.TileOffset..], geometry, header, pixels.Span, probabilities,
                        new byte[geometry.SampleCount / 16], new Av1BlockContext[geometry.ContextCount], default, initial, references, blocks,
                        filters, new sbyte[header.Cdef.IndexCount(geometry)], new Av1RestorationUnit[header.Restoration.UnitCount(geometry)], field,
                        segments is null ? default : segments.Span, previousMap);
                    tile.Decode();
                    Assert.Equal(geometry.MiRows, reader.ReadUInt16());
                    Assert.Equal(geometry.MiColumns, reader.ReadUInt16());
                    for (int cell = 0; cell < cells; cell++)
                    {
                        int id = reader.ReadByte();
                        bool skip = reader.ReadByte() != 0;
                        int reference = reader.ReadSByte();
                        int second = Math.Max(0, (int)reader.ReadSByte());
                        int transform = reader.ReadByte();
                        bool predicted = reader.ReadByte() != 0;
                        Assert.Equal(id, filters[cell].SegmentId);
                        Assert.Equal(skip, filters[cell].Skip);
                        Assert.Equal(reference, filters[cell].Reference);
                        Assert.Equal(second, blocks[cell].SecondReference);
                        if (conformance) { Assert.Equal(transform, filters[cell].LumaTransform); }
                        if (header.Segmentation.Enabled && header.Segmentation.UpdateMap) { Assert.Equal(id, segments!.Span[cell]); }
                        if (id != 0) { nonzero++; }
                        temporal |= predicted && header.Segmentation.TemporalUpdate;
                    }
                    if (header.Segmentation.Enabled && !header.Segmentation.UpdateMap)
                    {
                        inherited = true;
                        Assert.True(previousMap.SequenceEqual(segments!.Span));
                    }
                    if (conformance)
                    {
                        reset |= count > 0 && !header.Segmentation.Enabled;
                        Assert.Equal(!mixed, header.Segmentation.CodedLossless(header.Quantization));
                        if (header.Segmentation.Enabled)
                        {
                            Assert.Equal(-255, header.Segmentation.Segments[7].Quantizer);
                            Assert.Equal(-63, header.Segmentation.Segments[7].U);
                            Assert.True(header.Segmentation.PreSkip);
                        }
                    }
                    if (header.DisableFrameEndUpdate) { Av1TileProbabilities.Initialize(probabilities, header.Quantization.BaseIndex, initial); }
                    using Av1Picture picture = new(header, pixels.Publish());
                    int motionLength = Av1MotionField.StoredLength(geometry, header);
                    using WritableMediaBuffer? motion = motionLength == 0 ? null : allocator.Allocate(motionLength);
                    if (motion is not null) { Av1MotionField.Store(geometry, header, references, blocks, motion.Span); }
                    using MediaBuffer? frozenMotion = motion?.Publish();
                    using MediaBuffer? frozenSegments = segments?.Publish();
                    references.Save(picture, probabilities, allocator, frozenMotion, frozenSegments);
                    count++;
                }
            }
            Assert.Equal(expectedFrames, count);
            Assert.Equal(expectedNonzero, nonzero);
            if (conformance) { Assert.True(inherited && temporal && reset); }
            Assert.Equal(-1, source.ReadByte());
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void SegmentIdDeinterleavingMatchesEveryIndependentActiveRangeAndPrediction()
        {
            using GZipStream source = Open("segment-id.gz");
            using BinaryReader reader = new(source);
            Assert.Equal("AV1SID01"u8.ToArray(), reader.ReadBytes(8));
            Assert.Equal(288, reader.ReadInt32());
            for (int index = 0; index < 288; index++)
            {
                int difference = reader.ReadByte();
                int prediction = reader.ReadByte();
                int maximum = reader.ReadByte();
                Assert.Equal(reader.ReadByte(), Av1SegmentPrediction.Deinterleave(difference, prediction, maximum));
            }
            Assert.Equal(-1, source.ReadByte());
        }

        [Fact]
        public void FilterStrengthClipsBlockThenSegmentBeforeApplyingReferenceAndModeDeltas()
        {
            Av1Segments segments = default;
            segments[7] = new(30, 0, -63, 63, -40, 40, 0);
            Av1LoopFilterDeltas references = default;
            references[3] = 5;
            Av1LoopFilterParameters filter = new(50, 10, 40, 20, 0, true, references, -3, 4);
            Av1FilterBlock block = new() { Deltas = default };
            block.Deltas[0] = 40;
            block.Deltas[1] = -40;
            block.Deltas[2] = -50;
            block.Deltas[3] = 50;
            block.SetPrediction(3, true, false, 7);
            Av1Segmentation segmentation = new(true, true, false, segments);
            Assert.Equal(9, filter.Level(0, 0, block, segmentation));
            Assert.Equal(63, filter.Level(0, 1, block, segmentation));
            Assert.Equal(9, filter.Level(1, 0, block, segmentation));
            Assert.Equal(63, filter.Level(2, 0, block, segmentation));
        }

        private static GZipStream Open(string name) => new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", name)), CompressionMode.Decompress);
    }
}
