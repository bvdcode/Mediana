// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Drawing;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal static class TranslatedImageTestInput
    {
        internal static void Verify()
        {
            using PixelBuffer source = new(7, 9);
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    source[x, y] = new((byte)(x * 31), (byte)(y * 25), (byte)(x * y * 5), (byte)((x + y) % 3 * 127));
                }
            }
            Vector2[] offsets = [Vector2.Zero, new(3, 2), new(-4, -3), new(5, -2), new(-3, 4), new(40, -20)];
            foreach (Vector2 offset in offsets)
            {
                foreach (float opacity in new[] { 0f, .37f, 1f })
                {
                    foreach (ColorBlendMode mode in Enum.GetValues<ColorBlendMode>())
                    {
                        Compare(source, offset, opacity, mode, false);
                        Compare(source, offset, opacity, mode, true);
                    }
                }
            }
        }

        internal static void VerifyStridedFrame()
        {
            MediaBufferAllocator allocator = new(64);
            using WritableMediaBuffer storage = allocator.Allocate(27);
            storage.Memory.Span.Fill(179);
            new byte[] { 1, 2, 3, 255, 11, 22, 33, 255 }.CopyTo(storage.Memory.Span[3..]);
            new byte[] { 4, 5, 6, 255, 44, 55, 66, 255 }.CopyTo(storage.Memory.Span[15..]);
            using MediaBuffer data = storage.Publish();
            VideoFormat format = new(2, 2, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, SampleAspectRatio.Square);
            using VideoFrame frame = new(format, data, [new PlaneLayout(3, 12, 8, 2)]);
            using PixelBuffer output = new(3, 3);
            RasterCanvas canvas = new(output);
            canvas.DrawImage(frame, Matrix3x2.CreateTranslation(-1, 1));
            if (output[0, 1] != new Rgba32(11, 22, 33) || output[0, 2] != new Rgba32(44, 55, 66) || output[1, 1].A != 0)
            { throw new InvalidDataException("Translated frame ignored its plane offset or stride."); }
        }

        private static void Compare(PixelBuffer source, Vector2 offset, float opacity, ColorBlendMode mode, bool clip)
        {
            using PixelBuffer expected = new(11, 13), actual = new(11, 13);
            expected.Clear(new(80, 110, 170, 131));
            expected.Pixels.CopyTo(actual.Pixels);
            RasterCanvas reference = new(expected), canvas = new(actual);
            if (clip)
            {
                VectorPath path = new VectorPath().MoveTo(1, 1).LineTo(10, 5).LineTo(4, 11).Close();
                reference.ClipPath(path, Matrix3x2.Identity);
                canvas.ClipPath(path, Matrix3x2.Identity);
            }
            Matrix3x2 transform = Matrix3x2.CreateTranslation(offset);
            reference.ClipPath(new VectorPath().Rectangle(0, 0, source.Width, source.Height), transform);
            reference.DrawShader(point =>
            {
                Rgba32 color = source[(int)MathF.Floor(point.X), (int)MathF.Floor(point.Y)];
                return color with { A = (byte)MathF.Round(color.A * opacity) };
            }, transform, mode);
            canvas.DrawImage(source, transform, opacity, mode);
            if (!expected.Pixels.Span.SequenceEqual(actual.Pixels.Span))
            {
                throw new InvalidDataException("Translated image differs from clipped shader sampling.");
            }
        }
    }
}
