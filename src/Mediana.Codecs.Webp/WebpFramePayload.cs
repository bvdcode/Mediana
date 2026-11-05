// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Webp
{
    internal class WebpFramePayload : IDisposable
    {
        private readonly IWebpImagePayload image;
        private readonly VideoFrame? alphaSource;
        private readonly int alphaLength;

        private WebpFramePayload(IWebpImagePayload image, VideoFrame? alphaSource)
        {
            this.image = image;
            this.alphaSource = alphaSource;
            alphaLength = alphaSource is null ? 0 : checked(1 + alphaSource.Format.Width * alphaSource.Format.Height);
            Length = checked(WebpRiff.ChunkLength(image.PayloadLength) + (alphaSource is null ? 0 : WebpRiff.ChunkLength(alphaLength)));
        }

        internal int Length { get; }
        internal bool HasAlpha => image.HasAlpha;

        internal static WebpFramePayload Create(VideoFrame input, MediaBufferAllocator allocator, WebpEncodingOptions options, CancellationToken token)
        {
            IWebpImagePayload image = WebpEncoder.CreatePayload(input, allocator, options, token);
            VideoFrame? alphaSource = null;
            bool complete = false;
            try
            {
                if (options.Compression == WebpCompression.Lossy && image.HasAlpha) { alphaSource = input.Retain(); }
                WebpFramePayload result = new(image, alphaSource);
                complete = true;
                return result;
            }
            finally
            {
                if (!complete) { image.Dispose(); alphaSource?.Dispose(); }
            }
        }

        internal void Write(Memory<byte> output, CancellationToken token)
        {
            int position = 0;
            if (alphaSource is not null)
            {
                WebpRiff.WriteChunk(output.Span, "ALPH"u8, alphaLength);
                Span<byte> alpha = output.Span.Slice(WebpRiff.ChunkHeaderBytes, alphaLength);
                alpha[0] = 0;
                for (int row = 0; row < alphaSource.Format.Height; row++)
                {
                    token.ThrowIfCancellationRequested();
                    ReadOnlySpan<byte> source = alphaSource.GetRow(0, row);
                    for (int x = 0; x < alphaSource.Format.Width; x++) { alpha[1 + row * alphaSource.Format.Width + x] = source[x * 4 + 3]; }
                }
                position += WebpRiff.ChunkLength(alphaLength);
            }
            WebpRiff.WriteChunk(output.Span[position..], image.FourCc, image.PayloadLength);
            image.Write(output.Slice(position + WebpRiff.ChunkHeaderBytes, image.PayloadLength), token);
        }

        public void Dispose()
        {
            image.Dispose();
            alphaSource?.Dispose();
        }
    }
}
