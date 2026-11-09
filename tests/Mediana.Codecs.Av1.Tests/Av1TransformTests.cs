// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Text;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1TransformTests
    {
        [Fact]
        public void EveryTransformLengthMatchesIndependentAomReconstruction()
        {
            using FileStream file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "transform1d.bin.gz"));
            using GZipStream gzip = new(file, CompressionMode.Decompress);
            using BinaryReader reader = new(gzip, Encoding.UTF8, leaveOpen: true);
            Assert.Equal("AV1TXF1D", Encoding.ASCII.GetString(reader.ReadBytes(8)));
            int count = reader.ReadInt32();
            Assert.Equal(1932, count);
            Span<int> coefficients = stackalloc int[64];
            for (int sample = 0; sample < count; sample++)
            {
                int type = reader.ReadByte();
                int length = reader.ReadByte();
                for (int index = 0; index < length; index++) { coefficients[index] = reader.ReadInt32(); }
                Av1InverseTransform.Transform(coefficients[..length], type);
                for (int index = 0; index < length; index++) { Assert.Equal(reader.ReadInt32(), coefficients[index]); }
            }
            Assert.Equal(-1, gzip.ReadByte());
        }

        [Fact]
        public void RectangularTransformsAndFlipsMatchNativeOneDimensionalReconstruction()
        {
            using FileStream file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "transform2d.bin.gz"));
            using GZipStream gzip = new(file, CompressionMode.Decompress);
            using BinaryReader reader = new(gzip, Encoding.UTF8, leaveOpen: true);
            Assert.Equal("AV1TXF2D", Encoding.ASCII.GetString(reader.ReadBytes(8)));
            int count = reader.ReadInt32();
            Assert.Equal(772, count);
            Span<int> coefficients = stackalloc int[4096];
            for (int sample = 0; sample < count; sample++)
            {
                int type = reader.ReadByte();
                int width = reader.ReadByte();
                int height = reader.ReadByte();
                int length = width * height;
                for (int index = 0; index < length; index++) { coefficients[index] = reader.ReadInt32(); }
                Av1InverseTransform.Inverse(coefficients[..length], width, height, type);
                for (int index = 0; index < length; index++) { Assert.Equal(reader.ReadInt32(), coefficients[index]); }
            }
            Assert.Equal(-1, gzip.ReadByte());
        }

        [Fact]
        public void EveryFourPointTransformMatchesIndependentAomReconstruction()
        {
            using FileStream file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "transform4.bin.gz"));
            using GZipStream gzip = new(file, CompressionMode.Decompress);
            using BinaryReader reader = new(gzip, Encoding.UTF8, leaveOpen: true);
            Assert.Equal("AV1TXF04", Encoding.ASCII.GetString(reader.ReadBytes(8)));
            int count = reader.ReadInt32();
            Assert.Equal(903, count);
            Span<int> coefficients = stackalloc int[16];
            for (int sample = 0; sample < count; sample++)
            {
                int type = reader.ReadByte();
                for (int index = 0; index < coefficients.Length; index++)
                {
                    coefficients[index] = reader.ReadInt32();
                }
                Av1Transform4.Inverse(coefficients, type);
                for (int index = 0; index < coefficients.Length; index++)
                {
                    Assert.Equal(reader.ReadInt32(), coefficients[index]);
                }
            }
            Assert.Equal(-1, gzip.ReadByte());
        }
    }
}
