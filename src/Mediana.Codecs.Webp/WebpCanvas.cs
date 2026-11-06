// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Webp
{
    internal class WebpCanvas : IDisposable
    {
        private readonly WebpDocument document;
        private readonly MediaBufferAllocator allocator;
        private readonly WritableMediaBuffer canvas;
        private readonly VideoFormat format;
        private readonly PlaneLayout[] layout;
        private WebpDecodedFrame? last;
        internal WebpCanvas(WebpDocument document, ImageDecodeRequest request, MediaBufferAllocator allocator, CancellationToken token)
        {
            this.document = document; this.allocator = allocator;
            if (request.MaximumPixels is int limit && (long)document.Width * document.Height > limit) { throw new InvalidDataException("WebP source exceeds MaximumPixels."); }
            VideoColorDescription? color = document.Icc.IsEmpty ? null : new IccColorDescription(document.Icc.Span, false);
            format = new(document.Width, document.Height, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, SampleAspectRatio.Square, color);
            layout = format.CreateTightLayout(); canvas = allocator.Allocate(checked((int)layout[0].EndOffset));
            bool complete = false;
            try { if (document.Animated) { Clear(0, 0, document.Width, document.Height, token); } complete = true; }
            finally { if (!complete) { canvas.Dispose(); } }
        }
        internal void Draw(WebpDecodedFrame frame, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (last is { Dispose: true }) { Clear(last.Left, last.Top, last.Width, last.Height, token); }
            last = frame;
            using WritableMediaBuffer? block = document.Animated ? allocator.Allocate(checked(frame.Width * frame.Height * 4)) : null;
            Span<byte> decoded = block is null ? canvas.Memory.Span : block.Memory.Span;
            if (frame.Lossless)
            {
                using WebpDecodeWorkspace workspace = new(allocator);
                Span<uint> pixels = MemoryMarshal.Cast<byte, uint>(decoded);
                WebpLosslessDecoder.Decode(frame.Image.Span, pixels, frame.Width, frame.Height, workspace, token);
                for (int row = 0; row < frame.Height; row++)
                {
                    token.ThrowIfCancellationRequested();
                    for (int x = 0; x < frame.Width; x++)
                    {
                        uint value = pixels[row * frame.Width + x];
                        uint rgba = (value & 0xff00ff00u) | ((value & 255) << 16) | ((value >> 16) & 255);
                        pixels[row * frame.Width + x] = BitConverter.IsLittleEndian ? rgba : BinaryPrimitives.ReverseEndianness(rgba);
                    }
                }
            }
            else { throw new NotSupportedException("VP8 pixel decoding is not implemented."); }
            if (block is null) { return; }
            for (int y = 0; y < frame.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                Span<byte> target = canvas.Memory.Span.Slice(((frame.Top + y) * document.Width + frame.Left) * 4, frame.Width * 4);
                ReadOnlySpan<byte> source = decoded.Slice(y * frame.Width * 4, frame.Width * 4);
                if (frame.Replace) { source.CopyTo(target); continue; }
                for (int x = 0; x < frame.Width; x++) { Blend(source.Slice(x * 4, 4), target.Slice(x * 4, 4)); }
            }
        }
        private static void Blend(ReadOnlySpan<byte> source, Span<byte> target)
        {
            int alpha = source[3];
            if (alpha == 255 || target[3] == 0) { source.CopyTo(target); return; }
            if (alpha == 0) { return; }
            int retained = target[3] * (255 - alpha), denominator = alpha * 255 + retained;
            for (int c = 0; c < 3; c++) { target[c] = (byte)((source[c] * alpha * 255 + target[c] * retained + denominator / 2) / denominator); }
            target[3] = (byte)((denominator + 127) / 255);
        }
        private void Clear(int left, int top, int width, int height, CancellationToken token)
        {
            uint bgra = document.Background;
            uint rgba = (bgra & 0xff00ff00u) | ((bgra & 255) << 16) | ((bgra >> 16) & 255);
            if (!BitConverter.IsLittleEndian) { rgba = BinaryPrimitives.ReverseEndianness(rgba); }
            for (int row = top; row < top + height; row++)
            {
                token.ThrowIfCancellationRequested();
                MemoryMarshal.Cast<byte, uint>(canvas.Memory.Span.Slice((row * document.Width + left) * 4, width * 4)).Fill(rgba);
            }
        }
        internal VideoFrame Frame(long start, int duration, bool transfer)
        {
            using WritableMediaBuffer? copy = transfer ? null : allocator.Allocate(canvas.Memory.Length);
            if (copy is not null) { canvas.Memory.CopyTo(copy.Memory); }
            using MediaBuffer pixels = (copy ?? canvas).Publish();
            return new(format, pixels, layout, new(start, new(1, 1000)), document.Animated ? new(duration, new(1, 1000)) : null);
        }
        public void Dispose() => canvas.Dispose();
    }
}
