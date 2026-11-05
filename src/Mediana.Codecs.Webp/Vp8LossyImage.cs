// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Webp
{
    internal class Vp8LossyImage : IWebpImagePayload
    {
        private readonly WebpByteBuffer control;
        private readonly WebpByteBuffer coefficients;
        private int width;
        private int height;
        public int PayloadLength => checked(10 + control.Length + coefficients.Length);
        public bool HasAlpha { get; private set; }
        public ReadOnlySpan<byte> FourCc => "VP8 "u8;

        private Vp8LossyImage(MediaBufferAllocator allocator)
        {
            control = new(allocator);
            coefficients = new(allocator);
        }

        internal static Vp8LossyImage Create(VideoFrame input, int quality, MediaBufferAllocator allocator, CancellationToken token)
        {
            WebpEncoder.ValidateInput(input, token);
            ArgumentOutOfRangeException.ThrowIfNegative(quality);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);
            if (input.Format.Width >= 16384 || input.Format.Height >= 16384)
            {
                throw new NotSupportedException("VP8 dimensions cannot exceed 16383 pixels per axis.");
            }
            Vp8LossyImage result = new(allocator) { width = input.Format.Width, height = input.Format.Height };
            bool complete = false;
            try
            {
                result.Encode(input, Vp8Quantizer.FromQuality(quality), allocator, token);
                complete = true;
                return result;
            }
            finally
            {
                if (!complete) { result.Dispose(); }
            }
        }

        private void Encode(VideoFrame input, int quantizer, MediaBufferAllocator allocator, CancellationToken token)
        {
            Vp8BooleanWriter modes = new(control);
            Vp8BooleanWriter tokens = new(coefficients);
            WriteHeader(modes, quantizer);
            using Vp8Reconstruction reconstruction = new((width + 15) & ~15, allocator);
            Span<byte> source = stackalloc byte[384];
            Span<byte> prediction = stackalloc byte[16];
            Span<byte> top = stackalloc byte[8];
            Span<byte> leftPixels = stackalloc byte[4];
            Span<byte> chromaPrediction = stackalloc byte[128];
            Span<short> quantized = stackalloc short[16];
            Span<short> dequantized = stackalloc short[16];
            int lumaDc = Vp8ProbabilityTables.DcQuantizers[quantizer];
            int ac = Vp8ProbabilityTables.AcQuantizers[quantizer];
            for (int y = 0; y < height; y += 16)
            {
                reconstruction.Left.Clear();
                reconstruction.LeftModes.Clear();
                for (int x = 0; x < width; x += 16)
                {
                    token.ThrowIfCancellationRequested();
                    HasAlpha |= Vp8PixelSource.ReadMacroblock(input, x, y, source);
                    reconstruction.BeginMacroblock(x, y == 0);
                    modes.Write(false, 145);
                    for (int plane = 0; plane < 3; plane++)
                    {
                        if (plane == 1)
                        {
                            Vp8ChromaMode chromaMode = Vp8ChromaPrediction.Select(source[256..], reconstruction, x / 2, y == 0, chromaPrediction);
                            Vp8ChromaPrediction.Write(modes, chromaMode);
                        }
                        int columns = plane == 0 ? 4 : 2;
                        int sourceStride = columns * 4;
                        int sourceStart = plane switch { 0 => 0, 1 => 256, 2 => 320, _ => throw new InvalidOperationException() };
                        int canvasX = plane == 0 ? x : x / 2;
                        int leftStart = plane switch { 0 => 0, 1 => 4, 2 => 6, _ => throw new InvalidOperationException() };
                        int dc = plane == 0 ? lumaDc : Math.Min(lumaDc, 132);
                        for (int row = 0; row < columns; row++)
                        {
                            for (int column = 0; column < columns; column++)
                            {
                                int px = canvasX + column * 4;
                                int py = row * 4;
                                int offset = sourceStart + py * sourceStride + column * 4;
                                if (plane == 0)
                                {
                                    byte corner = reconstruction.LumaNeighbors(px, py, y == 0, top, leftPixels);
                                    Vp8IntraMode mode = Vp8IntraPrediction.Select(source, offset, sourceStride, top, leftPixels, corner, prediction);
                                    Vp8IntraPrediction.Write(modes, mode, reconstruction.AboveModes[px / 4], reconstruction.LeftModes[row]);
                                    reconstruction.AboveModes[px / 4] = (byte)mode;
                                    reconstruction.LeftModes[row] = (byte)mode;
                                }
                                else
                                {
                                    int start = (plane - 1) * 64 + py * 8 + column * 4;
                                    for (int line = 0; line < 4; line++) { chromaPrediction.Slice(start + line * 8, 4).CopyTo(prediction[(line * 4)..]); }
                                }
                                Vp8Transform.Quantize(source, offset, sourceStride, prediction, dc, ac, quantized, dequantized);
                                Vp8Transform.Reconstruct(dequantized, prediction, reconstruction.Plane(plane),
                                    py * reconstruction.Stride(plane) + px, reconstruction.Stride(plane));
                                int index = px / 4;
                                int left = leftStart + row;
                                int context = reconstruction.Above(plane)[index] + reconstruction.Left[left];
                                bool nonzero = Vp8CoefficientWriter.Write(tokens, quantized, plane == 0 ? 3 : 2, context);
                                reconstruction.Above(plane)[index] = nonzero ? (byte)1 : (byte)0;
                                reconstruction.Left[left] = nonzero ? (byte)1 : (byte)0;
                            }
                        }
                    }
                }
            }
            modes.Finish();
            tokens.Finish();
            if (control.Length >= 1 << 19) { throw new NotSupportedException("VP8 control partition exceeds its 19-bit size field."); }
        }

        private static void WriteHeader(Vp8BooleanWriter writer, int quantizer)
        {
            writer.Literal(0, 4);
            writer.Literal(0, 6);
            writer.Literal(0, 3);
            writer.Write(false);
            writer.Literal(0, 2);
            writer.Literal(quantizer, 7);
            writer.Literal(0, 5);
            writer.Write(false);
            foreach (byte probability in Vp8ProbabilityTables.CoefficientUpdates) { writer.Write(false, probability); }
            writer.Write(false);
        }

        public void Write(Memory<byte> output, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            int tag = (control.Length << 5) | 16;
            output.Span[0] = (byte)tag;
            output.Span[1] = (byte)(tag >> 8);
            output.Span[2] = (byte)(tag >> 16);
            ReadOnlySpan<byte> signature = [0x9d, 0x01, 0x2a];
            signature.CopyTo(output.Span[3..]);
            BinaryPrimitives.WriteUInt16LittleEndian(output.Span[6..], (ushort)width);
            BinaryPrimitives.WriteUInt16LittleEndian(output.Span[8..], (ushort)height);
            control.CopyTo(output.Span[10..]);
            coefficients.CopyTo(output.Span[(10 + control.Length)..]);
        }

        public void Dispose()
        {
            control.Dispose();
            coefficients.Dispose();
        }
    }
}
