// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Webp
{
    internal class WebpLosslessImage : IDisposable
    {
        private WebpEntropyStream? pixels;
        private WebpEntropyStream? predictors;
        private WebpEntropyStream? palette;
        private int colors;
        private bool alpha;
        private int width;
        private int height;
        internal int PayloadLength { get; private set; }
        internal bool HasAlpha => alpha;

        internal static WebpLosslessImage Create(VideoFrame input, MediaBufferAllocator allocator, CancellationToken token)
        {
            WebpLosslessImage result = new();
            bool complete = false;
            try
            {
                result.width = input.Format.Width;
                result.height = input.Format.Height;
                int columns = (result.width + (1 << WebpPredictor.BlockBits) - 1) >> WebpPredictor.BlockBits;
                int rows = (result.height + (1 << WebpPredictor.BlockBits) - 1) >> WebpPredictor.BlockBits;
                if (WebpPredictor.TryReadUniform(input, token, out uint uniform))
                {
                    result.alpha = uniform >> 24 != 255;
                    uint green = (uniform >> 8) & 255;
                    uint transformed = (((uniform & 0x00ff00ff) + 0x01000100 - (green | (green << 16))) & 0x00ff00ff) | (uniform & 0xff00ff00);
                    result.pixels = WebpEntropyStream.Create([transformed], 1, allocator, token);
                }
                else
                {
                    using WritableMediaBuffer residual = allocator.Allocate(checked(result.width * result.height * sizeof(uint)));
                    using WritableMediaBuffer modes = allocator.Allocate(checked(columns * rows * sizeof(uint)));
                    Span<uint> transformed = MemoryMarshal.Cast<byte, uint>(residual.Memory.Span);
                    Span<uint> prediction = MemoryMarshal.Cast<byte, uint>(modes.Memory.Span);
                    result.alpha = WebpPredictor.ReadPixels(input, transformed, token);
                    WebpPredictor.Transform(transformed, result.width, result.height, prediction, token);
                    result.predictors = WebpEntropyStream.Create(prediction, columns, allocator, token);
                    result.palette = WebpColorIndex.TryTransform(transformed, result.width, result.height, allocator, token, out result.colors, out int packedWidth);
                    result.pixels = WebpEntropyStream.Create(transformed[..(packedWidth * result.height)], packedWidth, allocator, token);
                }
                long bits = 3 + 1 + result.pixels.GetBitCount(true);
                if (result.predictors is not null) { bits += 6 + result.predictors.GetBitCount(false); }
                if (result.palette is not null) { bits += 11 + result.palette.GetBitCount(false); }
                result.PayloadLength = checked(5 + (int)((bits + 7) / 8));
                token.ThrowIfCancellationRequested();
                complete = true;
                return result;
            }
            finally
            {
                if (!complete) { result.Dispose(); }
            }
        }

        internal void Write(Memory<byte> output, CancellationToken token)
        {
            output.Span[0] = 0x2f;
            uint dimensions = (uint)(width - 1) | ((uint)(height - 1) << 14);
            if (alpha) { dimensions |= 1u << 28; }
            BinaryPrimitives.WriteUInt32LittleEndian(output.Span[1..], dimensions);
            WebpBitWriter writer = new(output[5..]);
            writer.Write(1, 1);
            writer.Write(2, 2);
            if (predictors is not null)
            {
                writer.Write(1, 1);
                writer.Write(0, 2);
                writer.Write(WebpPredictor.BlockBits - 2, 3);
                predictors.Write(writer, false, token);
            }
            if (palette is not null)
            {
                writer.Write(1, 1);
                writer.Write(3, 2);
                writer.Write((uint)(colors - 1), 8);
                palette.Write(writer, false, token);
            }
            writer.Write(0, 1);
            pixels!.Write(writer, true, token);
            writer.Complete();
            if (5 + (writer.BitCount + 7) / 8 != PayloadLength)
            {
                throw new InvalidOperationException("WebP entropy size does not match its encoded bit count.");
            }
        }

        public void Dispose()
        {
            pixels?.Dispose();
            predictors?.Dispose();
            palette?.Dispose();
        }
    }
}
