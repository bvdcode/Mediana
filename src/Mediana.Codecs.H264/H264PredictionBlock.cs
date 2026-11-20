// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264PredictionBlock
    {
        public static void Read(H264ReferencePicture reference, H264PictureStructure current, int plane, int x, int y, int width, int height,
            int motionX, int motionY, Span<byte> samples)
        {
            if (reference.Structure != reference.Picture.Structure || reference.Companion is not null)
            {
                throw new NotSupportedException("Prediction from a mixed frame/field reference view is not implemented yet.");
            }
            Read(reference.Picture, current, plane, x, y, width, height, motionX, motionY, samples);
        }

        public static void Read(H264Picture reference, H264PictureStructure current, int plane, int x, int y, int width, int height,
            int motionX, int motionY, Span<byte> samples)
        {
            if (plane is 1 or 2 && reference.ChromaVerticalScale == 2)
            {
                motionY += (reference.Structure, current) switch
                {
                    (H264PictureStructure.TopField, H264PictureStructure.BottomField) => 2,
                    (H264PictureStructure.BottomField, H264PictureStructure.TopField) => -2,
                    (H264PictureStructure.Frame, H264PictureStructure.Frame) or
                    (H264PictureStructure.TopField, H264PictureStructure.TopField) or
                    (H264PictureStructure.BottomField, H264PictureStructure.BottomField) => 0,
                    (H264PictureStructure.Frame, H264PictureStructure.TopField or H264PictureStructure.BottomField) or
                    (H264PictureStructure.TopField or H264PictureStructure.BottomField, H264PictureStructure.Frame) =>
                        throw new NotSupportedException("Mixed frame and field prediction requires a matching reference view."),
                    _ => throw new ArgumentOutOfRangeException(nameof(current))
                };
            }
            Read(reference, plane, x, y, width, height, motionX, motionY, samples);
        }

        public static void Read(H264Picture reference, int plane, int x, int y, int width, int height,
            int motionX, int motionY, Span<byte> samples)
        {
            switch (plane)
            {
                case 0:
                    H264MotionCompensation.Luma(reference, x, y, motionX, motionY, width, height, samples);
                    break;
                case 1:
                case 2:
                    H264MotionCompensation.Chroma(reference, plane, x / 2, y / reference.ChromaVerticalScale, motionX, motionY, width / 2, height / reference.ChromaVerticalScale, samples);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(plane));
            }
        }

        public static void Write(H264Picture target, int plane, int x, int y, int width, int height, ReadOnlySpan<byte> samples, Span<byte> prediction)
        {
            int destination = plane switch
            {
                0 => 0,
                1 => target.LumaMacroblockBytes,
                2 => target.LumaMacroblockBytes + target.ChromaMacroblockBytes,
                _ => throw new ArgumentOutOfRangeException(nameof(plane))
            };
            int scale = plane == 0 ? 1 : 2;
            int stride = 16 / scale * target.SampleBytes;
            int blockWidth = width / scale * target.SampleBytes;
            int verticalScale = target.VerticalScale(plane);
            int blockHeight = height / verticalScale;
            destination += (y % 16) / verticalScale * stride + (x % 16) / scale * target.SampleBytes;
            for (int row = 0; row < blockHeight; row++)
            {
                samples.Slice(row * blockWidth, blockWidth).CopyTo(prediction.Slice(destination + row * stride, blockWidth));
            }
        }
    }
}
