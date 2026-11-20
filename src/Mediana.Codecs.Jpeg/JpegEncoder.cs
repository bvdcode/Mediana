// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using System.Runtime.InteropServices;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.Jpeg
{
    public static class JpegEncoder
    {
        /// <summary>Writes baseline sequential JPEG from straight sRGB RGBA32. Retains the input and leaves the output open.
        /// Supports nonseekable streams; failed output must be discarded. Reserves 17,024 allocator bytes plus fixed block workspace.</summary>
        public static async Task<long> EncodeAsync(VideoFrame input, Stream destination, MediaBufferAllocator allocator,
            JpegEncodingOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            JpegEncodingOptions settings = options ?? new();
            settings.Validate();
            if (!destination.CanWrite) { throw new ArgumentException("JPEG output must be writable.", nameof(destination)); }
            if (input.Format.PixelFormat != VideoPixelFormat.Rgba32 || input.Format.GetColorInterpretation() != VideoColorInfo.Srgb)
            {
                throw new NotSupportedException("JPEG encoding requires straight RGBA32 pixels in full-range sRGB.");
            }
            if (input.Format.Width > ushort.MaxValue || input.Format.Height > ushort.MaxValue)
            {
                throw new NotSupportedException("JPEG dimensions cannot exceed 65535 samples per axis.");
            }
            SampleAspectRatio aspect = input.Format.SampleAspectRatio ?? throw new NotSupportedException("JPEG encoding requires a known sample aspect ratio.");
            if (aspect.Numerator > ushort.MaxValue || aspect.Denominator > ushort.MaxValue)
            {
                throw new NotSupportedException("The sample aspect ratio cannot be represented exactly in the JPEG JFIF density fields.");
            }
            (int horizontal, int vertical, bool monochrome) = settings.ChromaSampling switch
            {
                VideoChromaSampling.Monochrome => (1, 1, true),
                VideoChromaSampling.Sampling444 => (1, 1, false),
                VideoChromaSampling.Sampling422 => (2, 1, false),
                VideoChromaSampling.Sampling420 => (2, 2, false),
                _ => throw new ArgumentOutOfRangeException(nameof(options))
            };
            using VideoFrame retained = input.Retain();
            using WritableMediaBuffer storage = allocator.Allocate(16384 + 128 + 512);
            Memory<byte> quantization = storage.Memory.Slice(16384, 128);
            JpegEncodingTables.Quantization(settings.Quality, quantization.Span);
            Memory<byte> reciprocalBytes = storage.Memory[(16384 + 128)..];
            for (int i = 0; i < 128; i++) { MemoryMarshal.Cast<byte, float>(reciprocalBytes.Span)[i] = 1f / quantization.Span[(i & 64) + (i & 7) * 8 + (i & 63) / 8]; }
            JpegOutput output = new(destination, storage.Memory[..16384]);
            JpegEncodingHeaders.Write(output, input.Format, aspect, quantization.Span, horizontal, vertical, monochrome);
            JpegMcuEncoder encoder = new(output, horizontal, vertical, monochrome, settings.BackgroundColor);
            for (int y = 0; y < input.Format.Height; y += vertical * 8)
            {
                for (int x = 0; x < input.Format.Width; x += horizontal * 8)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!output.CanWriteMcu) { await output.FlushAsync(cancellationToken).ConfigureAwait(false); }
                    encoder.Write(retained, x, y, MemoryMarshal.Cast<byte, float>(reciprocalBytes.Span));
                }
            }
            output.FinishEntropy();
            output.Marker(217);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            return output.Written;
        }
    }
}
