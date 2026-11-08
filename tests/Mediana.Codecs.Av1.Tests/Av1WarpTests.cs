// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1WarpTests
    {
        [Fact]
        public void LeastSquaresModelsMatchIndependentNativeFitsAndShears()
        {
            using GZipStream source = Open("warp-fit.gz");
            using BinaryReader reader = new(source);
            Assert.Equal("AV1WFIT1"u8.ToArray(), reader.ReadBytes(8));
            int count = reader.ReadInt32();
            Assert.Equal(512, count);
            int valid = 0;
            for (int index = 0; index < count; index++)
            {
                int samples = reader.ReadByte();
                int size = reader.ReadByte();
                int row = reader.ReadUInt16();
                int column = reader.ReadUInt16();
                Av1MotionBlock motion = new() { Row = reader.ReadInt32(), Column = reader.ReadInt32() };
                Av1WarpSample[] candidates = new Av1WarpSample[samples];
                for (int sample = 0; sample < samples; sample++)
                {
                    candidates[sample] = new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                }
                Av1WarpParameters actual = Av1WarpEstimation.Estimate(candidates, row, column, 1 << (size / 3), 1 << (size / 3), motion);
                bool expectedValid = reader.ReadByte() != 0;
                Assert.Equal(expectedValid, actual.IsValid);
                if (!expectedValid) { continue; }
                Assert.Equal(ReadParameters(reader), actual);
                valid++;
            }
            Assert.Equal(442, valid);
            Assert.Equal(-1, source.ReadByte());
        }

        [Fact]
        public void WarpedFilteringMatchesIndependentLumaChromaAndClippedDestinations()
        {
            using GZipStream source = Open("warp-kernels.gz");
            using BinaryReader reader = new(source);
            Assert.Equal("AV1WARP1"u8.ToArray(), reader.ReadBytes(8));
            Assert.Equal(256, reader.ReadInt32());
            byte[] original = reader.ReadBytes(96 * 80 * 3 / 2);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (Av1PictureDecoder decoder = new(allocator))
            using (Av1Picture template = decoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "yuvNoise32.obu"))))
            using (WritableMediaBuffer write = allocator.Allocate(original.Length))
            using (WritableMediaBuffer probabilities = allocator.Allocate(1))
            {
                Av1SequenceHeader geometry = template.Sequence with { Width = 96, Height = 80 };
                original.CopyTo(write.Span);
                using Av1Picture picture = new(template.Header with { Geometry = geometry }, write.Publish());
                using MediaBuffer cdfs = probabilities.Publish();
                using Av1StoredPicture reference = new(picture, cdfs);
                byte[] output = new byte[geometry.SampleCount];
                for (int index = 0; index < 256; index++)
                {
                    Av1WarpParameters expected = ReadParameters(reader);
                    Av1WarpParameters actual = Av1WarpParameters.Create(expected.X, expected.Y, expected.A, expected.B, expected.C, expected.D);
                    Assert.Equal(expected, actual);
                    int plane = reader.ReadByte();
                    int x = reader.ReadByte();
                    int y = reader.ReadByte();
                    int width = reader.ReadByte();
                    int height = reader.ReadByte();
                    byte[] pixels = reader.ReadBytes(width * height);
                    output.AsSpan().Fill(237);
                    Av1WarpPrediction.Apply(output, geometry, reference, plane, x, y, width, height, actual);
                    int offset = geometry.PlaneOffset(plane);
                    int stride = geometry.PlaneStride(plane);
                    for (int sample = 0; sample < output.Length; sample++)
                    {
                        int local = sample - offset;
                        int row = local / stride - y;
                        int column = local % stride - x;
                        bool inside = local >= 0 && row >= 0 && row < height && column >= 0 && column < width;
                        Assert.Equal(inside ? pixels[row * width + column] : (byte)237, output[sample]);
                    }
                }
                Assert.Equal(original, reference.Pixels.Memory.ToArray());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(-1, source.ReadByte());
        }

        [Fact]
        public void WarpSamplesExcludeCompoundNeighborsAndReplaceLargeVectorsWithValidSamples()
        {
            Av1SequenceHeader sequence = default(Av1SequenceHeader) with { Width = 32, Height = 32 };
            Av1MotionBlock[] blocks = new Av1MotionBlock[sequence.MiRows * sequence.MiColumns];
            blocks.AsSpan().Fill(new() { Width = 2, Height = 2 });
            blocks[1 * sequence.MiColumns + 2] = new() { Width = 2, Height = 2, Reference = 1, SecondReference = 2 };
            blocks[2 * sequence.MiColumns + 1] = new() { Width = 2, Height = 2, Reference = 1, Row = 1000, Column = 1000 };
            blocks[1 * sequence.MiColumns + 1] = new() { Width = 2, Height = 2, Reference = 1, Row = -1000, Column = -1000 };
            blocks[1 * sequence.MiColumns + 4] = new() { Width = 2, Height = 2, Reference = 1 };
            Span<Av1WarpSample> storage = stackalloc Av1WarpSample[Av1WarpSamples.Capacity];
            Av1WarpSamples candidates = new(storage, blocks, sequence, 2, 2, 2, 2, new() { Reference = 1 });
            candidates.Find();
            Assert.Equal(new Av1WarpSample[] { new(24, 152, 24, 152) }, candidates.Samples.ToArray());
            blocks[1 * sequence.MiColumns + 4].Reference = 0;
            candidates = new(storage, blocks, sequence, 2, 2, 2, 2, new() { Reference = 1 });
            candidates.Find();
            Assert.Equal(new Av1WarpSample[] { new(88, 24, 1088, 1024) }, candidates.Samples.ToArray());
        }

        private static Av1WarpParameters ReadParameters(BinaryReader reader) => new(reader.ReadInt32(), reader.ReadInt32(),
            reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
            reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());

        private static GZipStream Open(string name) => new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", name)), CompressionMode.Decompress);
    }
}
