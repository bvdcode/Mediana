// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Webp
{
    internal class WebpFramePayload : IDisposable
    {
        private readonly IWebpImagePayload image;
        private readonly WebpAlphaImage? alpha;

        private WebpFramePayload(IWebpImagePayload image, WebpAlphaImage? alpha)
        {
            this.image = image;
            this.alpha = alpha;
            Length = checked(WebpRiff.ChunkLength(image.PayloadLength) + (alpha is null ? 0 : WebpRiff.ChunkLength(alpha.Length)));
        }

        internal int Length { get; }
        internal bool HasAlpha => image.HasAlpha;

        internal static WebpFramePayload Create(VideoFrame input, MediaBufferAllocator allocator, WebpEncodingOptions options, CancellationToken token)
        {
            IWebpImagePayload image = WebpEncoder.CreatePayload(input, allocator, options, token);
            WebpAlphaImage? alpha = null;
            bool complete = false;
            try
            {
                if (options.Compression == WebpCompression.Lossy && image.HasAlpha) { alpha = WebpAlphaImage.Create(input, allocator, token); }
                WebpFramePayload result = new(image, alpha);
                complete = true;
                return result;
            }
            finally
            {
                if (!complete) { image.Dispose(); alpha?.Dispose(); }
            }
        }

        internal void Write(Memory<byte> output, CancellationToken token)
        {
            int position = 0;
            if (alpha is not null)
            {
                WebpRiff.WriteChunk(output.Span, "ALPH"u8, alpha.Length);
                alpha.Write(output.Slice(WebpRiff.ChunkHeaderBytes, alpha.Length), token);
                position += WebpRiff.ChunkLength(alpha.Length);
            }
            WebpRiff.WriteChunk(output.Span[position..], image.FourCc, image.PayloadLength);
            image.Write(output.Slice(position + WebpRiff.ChunkHeaderBytes, image.PayloadLength), token);
        }

        public void Dispose()
        {
            image.Dispose();
            alpha?.Dispose();
        }
    }
}
