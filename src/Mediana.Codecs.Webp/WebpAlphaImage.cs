// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Webp
{
    internal class WebpAlphaImage : IDisposable
    {
        private readonly WebpLosslessImage? compressed;
        private readonly VideoFrame? source;

        private WebpAlphaImage(WebpLosslessImage? compressed, VideoFrame? source, int length)
        {
            this.compressed = compressed;
            this.source = source;
            Length = length;
        }

        internal int Length { get; }

        internal static WebpAlphaImage Create(VideoFrame input, MediaBufferAllocator allocator, CancellationToken token)
        {
            WebpLosslessImage? compressed = WebpLosslessImage.Create(input, allocator, token, WebpPixelProjection.Alpha);
            VideoFrame? source = null;
            bool complete = false;
            try
            {
                int rawLength = checked(1 + input.Format.Width * input.Format.Height);
                int compressedLength = compressed.PayloadLength - 4;
                if (compressedLength >= rawLength) { compressed.Dispose(); compressed = null; source = input.Retain(); }
                WebpAlphaImage result = new(compressed, source, compressed is null ? rawLength : compressedLength);
                complete = true;
                return result;
            }
            finally
            {
                if (!complete) { compressed?.Dispose(); source?.Dispose(); }
            }
        }

        internal void Write(Memory<byte> output, CancellationToken token)
        {
            if (compressed is not null)
            {
                output.Span[0] = 1;
                compressed.WriteImageStream(output[1..], token);
                return;
            }
            VideoFrame input = source ?? throw new ObjectDisposedException(nameof(WebpAlphaImage));
            output.Span[0] = 0;
            for (int row = 0; row < input.Format.Height; row++)
            {
                token.ThrowIfCancellationRequested();
                ReadOnlySpan<byte> pixels = input.GetRow(0, row);
                for (int x = 0; x < input.Format.Width; x++) { output.Span[1 + row * input.Format.Width + x] = pixels[x * 4 + 3]; }
            }
        }

        public void Dispose()
        {
            compressed?.Dispose();
            source?.Dispose();
        }
    }
}
