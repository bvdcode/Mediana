// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;

namespace Mediana.Codecs.H264
{
    internal readonly ref struct H264ReferencePlane
    {
        private readonly ReadOnlySpan<byte> companion;
        private readonly int sampleBytes;

        public H264ReferencePlane(H264ReferencePicture reference, int plane)
        {
            H264Picture picture = reference.Picture;
            Columns = picture.Stride(plane);
            Rows = picture.Height / picture.VerticalScale(plane);
            Stride = Columns;
            sampleBytes = picture.SampleBytes;
            Samples = picture.ReadPlane(plane);
            companion = [];
            switch (reference.Structure, picture.Structure)
            {
                case (H264PictureStructure.Frame, H264PictureStructure.Frame):
                case (H264PictureStructure.TopField, H264PictureStructure.TopField):
                case (H264PictureStructure.BottomField, H264PictureStructure.BottomField):
                    break;
                case (H264PictureStructure.TopField, H264PictureStructure.Frame):
                    Rows /= 2;
                    Stride *= 2;
                    break;
                case (H264PictureStructure.BottomField, H264PictureStructure.Frame):
                    Samples = Samples[(Columns * sampleBytes)..];
                    Rows /= 2;
                    Stride *= 2;
                    break;
                case (H264PictureStructure.Frame, H264PictureStructure.TopField):
                    H264Picture bottom = reference.Companion ?? throw new InvalidDataException("A frame reference requires both fields.");
                    if (bottom.Structure != H264PictureStructure.BottomField || bottom.Sequence != picture.Sequence)
                    {
                        throw new InvalidDataException("A frame reference requires matching complementary field geometry.");
                    }
                    companion = bottom.ReadPlane(plane);
                    Rows *= 2;
                    break;
                default:
                    throw new InvalidDataException("The H.264 reference view does not match its stored picture structure.");
            }
        }

        public ReadOnlySpan<byte> Samples { get; }
        public int Columns { get; }
        public int Rows { get; }
        public int Stride { get; }
        public bool IsPaired => !companion.IsEmpty;

        public void Copy(long x, long y, int width, int height, Span<byte> output)
        {
            for (int row = 0; row < height; row++)
            {
                int sourceRow = (int)Math.Clamp(y + row, 0, Rows - 1);
                ReadOnlySpan<byte> source = Samples;
                if (IsPaired)
                {
                    if ((sourceRow & 1) != 0)
                    {
                        source = companion;
                    }
                    sourceRow /= 2;
                }
                source = source.Slice(sourceRow * Stride * sampleBytes, Columns * sampleBytes);
                Span<byte> destination = output.Slice(row * width * sampleBytes, width * sampleBytes);
                if (sampleBytes == 1)
                {
                    H264BorderExtension.Copy(source, Columns, 1, x, 0, width, 1, destination);
                }
                else
                {
                    H264HighPrecisionInterpolation.Copy(MemoryMarshal.Cast<byte, ushort>(source), Columns, Columns, 1,
                        x, 0, width, 1, MemoryMarshal.Cast<byte, ushort>(destination));
                }
            }
        }
    }
}
