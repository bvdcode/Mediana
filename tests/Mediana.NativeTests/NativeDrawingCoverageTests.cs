// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Core.Memory;
using Mediana.Drawing;
using Mediana.Drawing.Fonts;

namespace Mediana.NativeTests
{
    internal static class NativeDrawingCoverageTests
    {
        internal static void Run()
        {
            using PixelBuffer pixels = new(96, 96);
            RasterCanvas canvas = new(pixels);
            canvas.FillPathAntialiased(new VectorPath().Rectangle(.25f, .5f, 3.5f, 2.75f), new(23, 57, 139), Matrix3x2.Identity);
            if (pixels[0, 0].A != 96) { throw new InvalidDataException("Fractional fill coverage differs."); }
            canvas.StrokePathAntialiased(new VectorPath().MoveTo(5, 5).LineTo(25, 5).LineTo(25, 25), new(23, 57, 139), new() { Width = 4 }, Matrix3x2.Identity);
            TextRenderer renderer = new();
            TrueTypeFont font = renderer.ResolveFont(new("Canada1500"))!;
            int glyph = font.Glyph('Я');
            renderer.DrawGlyphAntialiased(canvas, font, glyph, new(23, 57, 139),
                Matrix3x2.CreateScale(25 / font.Units, -25 / font.Units) * Matrix3x2.CreateTranslation(10.12345f, 70.12345f));
            using Mediana.Core.Video.VideoFrame frame = pixels.PublishVideoFrame();
            using PixelBuffer next = new(96, 96);
            if (next[0, 0].A != 0 || frame.Data.Span[3] != 96) { throw new InvalidDataException("Pooled pixel ownership differs."); }
            MediaBufferAllocator allocator = new(32, System.Buffers.MemoryPool<byte>.Shared);
            using WritableMediaBuffer buffer = allocator.Allocate(13);
            if (buffer.Memory.Length != 13 || allocator.AllocatedBytes < 13) { throw new InvalidDataException("Pooled budget accounting differs."); }
        }
    }
}
