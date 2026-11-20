// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264MixedPredictionTests
    {
        [Theory]
        [InlineData(8, 1)]
        [InlineData(8, 2)]
        [InlineData(10, 1)]
        [InlineData(10, 2)]
        public void MixedViewsMatchMaterialPicturesAtEveryFractionAndBorder(int depth, int chroma)
        {
            H264SequenceParameterSet sequence = new()
            {
                WidthInMbs = 3,
                HeightInMapUnits = 2,
                Width = 48,
                Height = 64,
                LumaBitDepth = depth,
                ChromaBitDepth = depth,
                ChromaFormat = chroma
            };
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264Picture frame = new(sequence, allocator, H264SliceKind.P))
            using (H264Picture top = new(sequence, allocator, H264SliceKind.P, H264PictureStructure.TopField))
            using (H264Picture bottom = new(sequence, allocator, H264SliceKind.P, H264PictureStructure.BottomField))
            {
                Populate(frame, top, bottom);
                H264ReferencePicture material = new(frame, 0, new(0, 1));
                H264ReferencePicture topReference = new(top, 0, new(0, 0, H264PictureStructure.TopField));
                H264ReferencePicture bottomReference = new(bottom, 0, new(0, 1, H264PictureStructure.BottomField))
                {
                    FrameIdentity = topReference.FrameIdentity
                };
                long before = allocator.AllocatedBytes;
                Verify(material.GetField(H264PictureStructure.TopField), top);
                Verify(material.GetField(H264PictureStructure.BottomField), bottom);
                Verify(H264ReferencePicture.GetFrame(topReference, bottomReference), frame);
                Assert.Equal(before, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static void Populate(H264Picture frame, H264Picture top, H264Picture bottom)
        {
            for (int plane = 0; plane < 3; plane++)
            {
                int stride = frame.Stride(plane);
                int rows = frame.Height / frame.VerticalScale(plane);
                Span<byte> data = frame.Plane(plane);
                for (int y = 0; y < rows; y++)
                {
                    for (int x = 0; x < stride; x++)
                    {
                        int value = (x * 67 + y * 31 + x * y * 7 + plane * 53) & frame.MaximumSample;
                        if (frame.SampleBytes == 1)
                        {
                            data[y * stride + x] = (byte)value;
                        }
                        else
                        {
                            MemoryMarshal.Cast<byte, ushort>(data)[y * stride + x] = (ushort)value;
                        }
                    }
                    Span<byte> destination = (y & 1) == 0 ? top.Plane(plane) : bottom.Plane(plane);
                    int rowBytes = stride * frame.SampleBytes;
                    data.Slice(y * rowBytes, rowBytes).CopyTo(destination.Slice(y / 2 * rowBytes, rowBytes));
                }
            }
        }

        private static void Verify(H264ReferencePicture view, H264Picture material)
        {
            Span<byte> actualBuffer = stackalloc byte[512];
            Span<byte> expectedBuffer = stackalloc byte[512];
            int[] positionsX = [-16, 0, 28, 48];
            int[] positionsY = [-16, 0, 16, 32, 64];
            for (int plane = 0; plane < 3; plane++)
            {
                int horizontalScale = plane == 0 ? 1 : 2;
                int length = 16 / horizontalScale * (16 / material.VerticalScale(plane)) * material.SampleBytes;
                Span<byte> actual = actualBuffer[..length];
                Span<byte> expected = expectedBuffer[..length];
                foreach (int x in positionsX)
                {
                    foreach (int y in positionsY)
                    {
                        for (int dx = -8; dx < 8; dx++)
                        {
                            for (int dy = -8; dy < 8; dy++)
                            {
                                H264PredictionBlock.Read(view, view.Structure, plane, x, y, 16, 16, dx, dy, actual);
                                H264PredictionBlock.Read(material, material.Structure, plane, x, y, 16, 16, dx, dy, expected);
                                if (!actual.SequenceEqual(expected))
                                {
                                    Assert.Fail($"Reference mismatch: {view.Structure}, plane {plane}, ({x}, {y}), motion ({dx}, {dy}).");
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
