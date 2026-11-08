// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1GlobalMotionTests
    {
        [Fact]
        public void GlobalModelsOwnAllSevenValuesAndCompareTheirCompleteState()
        {
            Av1GlobalMotionModels models = default;
            models[6] = new(Av1GlobalMotionType.Translation, new(8192, -16384, 65536, 0, 0, 65536));
            Av1GlobalMotion first = new(models);
            Av1GlobalMotion copy = first;
            models[6] = default;
            Assert.NotEqual(default(Av1GlobalMotion), first);
            Assert.Equal(first, copy);
            Assert.Equal(first.GetHashCode(), copy.GetHashCode());
            Assert.Equal(8192, first[7].Parameter(0));
            Assert.Equal(-16384, first[7].Parameter(1));
            Assert.Equal(default(Av1GlobalMotion), new(models));
        }

        [Fact]
        public void GlobalVectorsMatchIndependentModelsAtEveryBlockGeometryAndPrecision()
        {
            using GZipStream source = Open("global-mv.gz");
            using BinaryReader reader = new(source);
            Assert.Equal("AV1GMV01"u8.ToArray(), reader.ReadBytes(8));
            Assert.Equal(1024, reader.ReadInt32());
            for (int index = 0; index < 1024; index++)
            {
                Av1GlobalMotionType type = (Av1GlobalMotionType)reader.ReadByte();
                bool highPrecision = reader.ReadByte() != 0;
                bool integer = reader.ReadByte() != 0;
                int width = reader.ReadByte();
                int height = reader.ReadByte();
                int row = reader.ReadUInt16();
                int column = reader.ReadUInt16();
                Av1WarpParameters parameters = new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                Av1MotionCandidate actual = new Av1GlobalMotionModel(type, parameters).Motion(row, column, width, height, integer, highPrecision);
                Assert.Equal(reader.ReadInt32(), actual.Row);
                Assert.Equal(reader.ReadInt32(), actual.Column);
            }
            Assert.Equal(-1, source.ReadByte());
        }

        [Fact]
        public void CompoundGlobalWarpMatchesIndependentWeightedLumaChromaAndClippedPredictions()
        {
            using GZipStream source = Open("global-warp.gz");
            using BinaryReader reader = new(source);
            Assert.Equal("AV1GWAR1"u8.ToArray(), reader.ReadBytes(8));
            Assert.Equal(192, reader.ReadInt32());
            byte[] firstPixels = reader.ReadBytes(96 * 80 * 3 / 2);
            byte[] secondPixels = reader.ReadBytes(firstPixels.Length);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (Av1PictureDecoder decoder = new(allocator))
            using (Av1Picture template = decoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "yuvNoise32.obu"))))
            using (WritableMediaBuffer firstWrite = allocator.Allocate(firstPixels.Length))
            using (WritableMediaBuffer secondWrite = allocator.Allocate(secondPixels.Length))
            using (WritableMediaBuffer probabilities = allocator.Allocate(1))
            {
                Av1SequenceHeader geometry = template.Sequence with { Width = 96, Height = 80 };
                firstPixels.CopyTo(firstWrite.Span);
                secondPixels.CopyTo(secondWrite.Span);
                using Av1Picture firstPicture = new(template.Header with { Geometry = geometry }, firstWrite.Publish());
                using Av1Picture secondPicture = new(template.Header with { Geometry = geometry }, secondWrite.Publish());
                using MediaBuffer cdfs = probabilities.Publish();
                using Av1StoredPicture first = new(firstPicture, cdfs);
                using Av1StoredPicture second = new(secondPicture, cdfs);
                byte[] output = new byte[geometry.SampleCount];
                for (int index = 0; index < 192; index++)
                {
                    Av1WarpParameters firstWarp = ReadWarp(reader);
                    Av1WarpParameters secondWarp = ReadWarp(reader);
                    int plane = reader.ReadByte();
                    int x = reader.ReadByte();
                    int y = reader.ReadByte();
                    int width = reader.ReadByte();
                    int height = reader.ReadByte();
                    Av1CompoundPrediction prediction = new(new(reader.ReadByte(), reader.ReadByte()));
                    byte[] expected = reader.ReadBytes(width * height);
                    output.AsSpan().Fill(237);
                    Av1MotionCompensation.ApplyCompound(output, geometry, first, second, plane, x, y, width, height,
                        default, prediction, width, height, firstWarp: firstWarp, secondWarp: secondWarp);
                    int offset = geometry.PlaneOffset(plane);
                    int stride = geometry.PlaneStride(plane);
                    for (int sample = 0; sample < output.Length; sample++)
                    {
                        int local = sample - offset;
                        int row = local / stride - y;
                        int column = local % stride - x;
                        bool inside = local >= 0 && row >= 0 && row < height && column >= 0 && column < width;
                        Assert.Equal(inside ? expected[row * width + column] : (byte)237, output[sample]);
                    }
                }
                Assert.Equal(firstPixels, first.Pixels.Memory.ToArray());
                Assert.Equal(secondPixels, second.Pixels.Memory.ToArray());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(-1, source.ReadByte());
        }

        [Theory]
        [InlineData("globalPan", 12)]
        [InlineData("globalFeatures", 11)]
        public void EveryDecodedGlobalMatrixMatchesIndependentFrameMetadata(string fixture, int expectedFrames)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", fixture + ".obu"));
            using GZipStream source = Open(fixture + ".global.gz");
            using BinaryReader reader = new(source);
            Assert.Equal("AV1GLOB1"u8.ToArray(), reader.ReadBytes(8));
            Assert.Equal(expectedFrames, reader.ReadInt32());
            int nonidentity = 0;
            int types = 0;
            bool invalidWarp = false;
            int count = 0;
            Av1GlobalMotion previous = default;
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            using (Av1PictureDecoder decoder = new(allocator))
            {
                Av1FrameUnitReader units = new(encoded);
                while (units.TryRead(out ReadOnlySpan<byte> unit, default))
                {
                    using Av1Picture picture = decoder.Decode(unit);
                    Assert.Equal(count, reader.ReadInt64());
                    Assert.Equal(picture.Header.ShowFrame, reader.ReadByte() != 0);
                    Assert.Equal(0, reader.ReadByte());
                    Assert.Equal(picture.Header.Inter.HighPrecisionMotion, reader.ReadByte() != 0);
                    Assert.Equal(picture.Header.Inter.IntegerMotion, reader.ReadByte() != 0);
                    Assert.Equal(picture.Header.Inter.PrimaryReference, reader.ReadByte());
                    for (int reference = 1; reference <= Av1ReferenceIndices.Count; reference++)
                    {
                        Av1GlobalMotionModel actual = picture.Header.GlobalMotion[reference];
                        Assert.Equal((Av1GlobalMotionType)reader.ReadByte(), actual.Type);
                        for (int parameter = 0; parameter < 6; parameter++) { Assert.Equal(reader.ReadInt32(), actual.Parameter(parameter)); }
                        if (actual.Type != Av1GlobalMotionType.Identity) { nonidentity++; }
                        types |= 1 << (int)actual.Type;
                        invalidWarp |= actual.InvalidWarp;
                    }
                    if (fixture == "globalFeatures" && count == 5) { Assert.Equal(previous, picture.Header.GlobalMotion); }
                    if (fixture == "globalFeatures" && count == 10) { Assert.Equal(default(Av1GlobalMotion), picture.Header.GlobalMotion); }
                    previous = picture.Header.GlobalMotion;
                    count++;
                }
            }
            Assert.Equal(expectedFrames, count);
            Assert.True(nonidentity > 0);
            if (fixture == "globalFeatures") { Assert.Equal(15, types); Assert.True(invalidWarp); }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(-1, source.ReadByte());
        }

        private static Av1WarpParameters ReadWarp(BinaryReader reader) => new(reader.ReadInt32(), reader.ReadInt32(),
            reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
            reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());

        private static GZipStream Open(string name) => new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", name)), CompressionMode.Decompress);
    }
}
