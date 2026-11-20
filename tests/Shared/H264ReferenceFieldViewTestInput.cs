// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264ReferenceFieldViewTestInput
    {
        public static void Verify()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            foreach (int depth in new[] { 8, 10 })
            {
                foreach (int chroma in new[] { 1, 2 })
                {
                    foreach (int? longTerm in new int?[] { null, 3 })
                    {
                        H264SequenceParameterSet sequence = new()
                        {
                            WidthInMbs = 2,
                            HeightInMapUnits = 2,
                            Width = 32,
                            Height = 64,
                            LumaBitDepth = depth,
                            ChromaBitDepth = depth,
                            ChromaFormat = chroma
                        };
                        VerifyMaterial(allocator, sequence, longTerm);
                        VerifyPaired(allocator, sequence, longTerm);
                    }
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Reference field views retained picture buffers.");
            }
        }

        private static void VerifyMaterial(MediaBufferAllocator allocator, H264SequenceParameterSet sequence, int? longTerm)
        {
            using H264Picture picture = new(sequence, allocator, H264SliceKind.P);
            Fill(picture);
            H264ReferencePicture frame = new(picture, 7, new(4, 5), longTerm);
            VerifyFields(allocator, frame, picture, picture);
        }

        private static void VerifyPaired(MediaBufferAllocator allocator, H264SequenceParameterSet sequence, int? longTerm)
        {
            using H264Picture topPicture = new(sequence, allocator, H264SliceKind.P, H264PictureStructure.TopField);
            using H264Picture bottomPicture = new(sequence, allocator, H264SliceKind.P, H264PictureStructure.BottomField);
            Fill(topPicture);
            Fill(bottomPicture);
            H264ReferencePicture top = new(topPicture, 7, new(4, 0, H264PictureStructure.TopField), longTerm);
            H264ReferencePicture bottom = new(bottomPicture, 7, new(0, 5, H264PictureStructure.BottomField), longTerm)
            {
                FrameIdentity = top.FrameIdentity
            };
            H264ReferencePicture frame = H264ReferencePicture.GetFrame(top, bottom);
            VerifyFields(allocator, frame, topPicture, bottomPicture);
        }

        private static void VerifyFields(MediaBufferAllocator allocator, H264ReferencePicture frame,
            H264Picture topPicture, H264Picture bottomPicture)
        {
            long allocated = allocator.AllocatedBytes;
            H264ReferencePicture top = frame.GetField(H264PictureStructure.TopField);
            H264ReferencePicture bottom = frame.GetField(H264PictureStructure.BottomField);
            if (top.Picture != topPicture || bottom.Picture != bottomPicture ||
                top.Companion is not null || bottom.Companion is not null ||
                top.FrameIdentity != frame.FrameIdentity || bottom.FrameIdentity != frame.FrameIdentity ||
                top.FrameNumber != frame.FrameNumber || bottom.FrameNumber != frame.FrameNumber ||
                top.LongTermIndex != frame.LongTermIndex || bottom.LongTermIndex != frame.LongTermIndex ||
                top.Order.Value != 4 || bottom.Order.Value != 5 ||
                top.Identity != frame.FrameIdentity.ForStructure(H264PictureStructure.TopField) ||
                bottom.Identity != frame.FrameIdentity.ForStructure(H264PictureStructure.BottomField))
            {
                throw new InvalidDataException("A reference field view changed its physical picture, identity or reference metadata.");
            }
            VerifySamples(top, 17);
            VerifySamples(bottom, 113);
            if (!ReferenceEquals(top, top.GetField(H264PictureStructure.TopField)) ||
                !ReferenceEquals(bottom, bottom.GetField(H264PictureStructure.BottomField)) ||
                allocator.AllocatedBytes != allocated)
            {
                throw new InvalidOperationException("Reference field selection must reuse pixel storage and existing field views.");
            }
        }

        private static void Fill(H264Picture picture)
        {
            for (int plane = 0; plane < 3; plane++)
            {
                int rowBytes = picture.Stride(plane) * picture.SampleBytes;
                Span<byte> samples = picture.Plane(plane);
                for (int row = 0; row < picture.Height / picture.VerticalScale(plane); row++)
                {
                    int value = picture.Structure switch
                    {
                        H264PictureStructure.Frame => row % 2 == 0 ? 17 : 113,
                        H264PictureStructure.TopField => 17,
                        H264PictureStructure.BottomField => 113,
                        _ => throw new ArgumentOutOfRangeException(nameof(picture))
                    };
                    Span<byte> line = samples.Slice(row * rowBytes, rowBytes);
                    if (picture.SampleBytes == 1)
                    {
                        line.Fill((byte)value);
                    }
                    else
                    {
                        MemoryMarshal.Cast<byte, ushort>(line).Fill((ushort)value);
                    }
                }
            }
        }

        private static void VerifySamples(H264ReferencePicture reference, int expected)
        {
            Span<byte> storage = stackalloc byte[64];
            for (int plane = 0; plane < 3; plane++)
            {
                H264ReferencePlane view = new(reference, plane);
                Span<byte> row = storage[..(view.Columns * reference.Picture.SampleBytes)];
                for (int y = 0; y < view.Rows; y++)
                {
                    view.Copy(0, y, view.Columns, 1, row);
                    bool matches = reference.Picture.SampleBytes switch
                    {
                        1 => row.IndexOfAnyExcept((byte)expected) < 0,
                        2 => MemoryMarshal.Cast<byte, ushort>(row).IndexOfAnyExcept((ushort)expected) < 0,
                        _ => throw new InvalidDataException("Invalid H.264 sample storage size.")
                    };
                    if (!matches)
                    {
                        throw new InvalidDataException("A reference view selected samples from the opposite field.");
                    }
                }
            }
        }
    }
}
