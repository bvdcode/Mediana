// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Codecs.Gif
{
    internal class GifCanvas : IDisposable
    {
        private readonly GifDocument document;
        private readonly MediaBufferAllocator allocator;
        private readonly WritableMediaBuffer canvas;
        private WritableMediaBuffer? previous;
        private GifFrame? last;
        private readonly VideoFormat format;
        private readonly PlaneLayout[] layout;

        internal GifCanvas(GifDocument document, MediaBufferAllocator allocator, ImageDecodeRequest request, CancellationToken token)
        {
            this.document = document; this.allocator = allocator;
            if (request.MaximumPixels is int limit && (long)document.Width * document.Height > limit)
            { throw new InvalidDataException("GIF source exceeds MaximumPixels."); }
            SampleAspectRatio aspect = document.Aspect == 0 ? SampleAspectRatio.Square : new(document.Aspect + 15, 64);
            format = new(document.Width, document.Height, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, aspect);
            layout = format.CreateTightLayout();
            canvas = allocator.Allocate(checked((int)layout[0].EndOffset));
            bool initialized = false;
            try { Clear(0, 0, document.Width, document.Height, document.Frames[0].TransparentIndex >= 0, token); initialized = true; }
            finally { if (!initialized) { canvas.Dispose(); } }
        }
        internal void Draw(GifFrame frame, CancellationToken token)
        {
            if (last is not null)
            {
                switch (last.Disposal)
                {
                    case 0:
                    case 1: break;
                    case 2: Clear(last.Left, last.Top, last.Width, last.Height, last.TransparentIndex >= 0, token); break;
                    case 3:
                        CopyRegion(last, previous!.Memory.Span, false, token);
                        previous.Dispose(); previous = null;
                        break;
                    default: throw new InvalidDataException("Unsupported GIF disposal.");
                }
            }
            last = frame;
            if (frame.Disposal == 3)
            {
                previous = allocator.Allocate(checked(frame.Width * frame.Height * 4));
                CopyRegion(frame, previous.Memory.Span, true, token);
            }
            GifLzw.Paint(frame, canvas.Memory.Span, document.Width, token);
        }
        private void CopyRegion(GifFrame frame, Span<byte> saved, bool save, CancellationToken token)
        {
            for (int y = 0; y < frame.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                Span<byte> row = canvas.Memory.Span.Slice(((frame.Top + y) * document.Width + frame.Left) * 4, frame.Width * 4);
                Span<byte> backup = saved.Slice(y * frame.Width * 4, frame.Width * 4);
                if (save) { row.CopyTo(backup); } else { backup.CopyTo(row); }
            }
        }
        private void Clear(int left, int top, int width, int height, bool transparent, CancellationToken token)
        {
            Span<byte> color = stackalloc byte[4];
            color.Clear();
            if (!transparent && document.BackgroundIndex < document.Palette.Length / 3)
            { document.Palette.Span.Slice(document.BackgroundIndex * 3, 3).CopyTo(color); color[3] = 255; }
            for (int y = top; y < top + height; y++)
            {
                token.ThrowIfCancellationRequested();
                Span<byte> row = canvas.Memory.Span.Slice((y * document.Width + left) * 4, width * 4);
                for (int x = 0; x < width; x++) { color.CopyTo(row[(x * 4)..]); }
            }
        }
        internal VideoFrame Frame(long start, int duration, bool transfer)
        {
            using WritableMediaBuffer? copy = transfer ? null : allocator.Allocate(canvas.Memory.Length);
            if (copy is not null) { canvas.Memory.CopyTo(copy.Memory); }
            using MediaBuffer pixels = (copy ?? canvas).Publish();
            return new(format, pixels, layout, new(start, new(1, 100)), new(duration, new(1, 100)));
        }
        public void Dispose() { previous?.Dispose(); canvas.Dispose(); }
    }
}
