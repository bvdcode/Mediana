// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Codecs.Webp
{
    /// <summary>Streams lossless, lossy or mixed WebP animation frames with bounded per-frame workspace.</summary>
    public static class WebpAnimationEncoder
    {
        private const int HeaderBytes = 44;
        private const int FrameHeaderBytes = 24;
        private static readonly WebpEncodingOptions DefaultEncoding = new();

        /// <summary>Encodes borrowed full-canvas lossless frames in display order. Leaves the seekable destination open.
        /// Frames need identical dimensions and exact integer-millisecond durations. Failed output must be discarded.</summary>
        public static Task<long> EncodeAsync(IAsyncEnumerable<VideoFrame> frames, Stream destination, MediaBufferAllocator allocator,
            ushort loopCount = 0, CancellationToken cancellationToken = default) =>
            EncodeAsync(frames, destination, allocator, DefaultEncoding, loopCount, cancellationToken);

        /// <summary>Encodes full-canvas replacement frames using explicit compression, quality and metadata.</summary>
        public static Task<long> EncodeAsync(IAsyncEnumerable<VideoFrame> frames, Stream destination, MediaBufferAllocator allocator,
            WebpEncodingOptions encoding, ushort loopCount = 0, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(frames);
            return EncodeCoreAsync(Wrap(frames, cancellationToken), destination, allocator, encoding, loopCount, null, cancellationToken);
        }

        /// <summary>Encodes borrowed rectangles with explicit canvas, blending, disposal and optional per-frame compression.
        /// Only one frame's compression workspace is live. Destination must seek to complete the RIFF size and alpha flag.</summary>
        public static Task<long> EncodeAsync(IAsyncEnumerable<WebpAnimationFrame> frames, Stream destination, MediaBufferAllocator allocator,
            WebpAnimationOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            options.Validate();
            return EncodeCoreAsync(frames, destination, allocator, options.Encoding, options.LoopCount, options, cancellationToken);
        }

        private static async Task<long> EncodeCoreAsync(IAsyncEnumerable<WebpAnimationFrame> frames, Stream destination, MediaBufferAllocator allocator,
            WebpEncodingOptions encoding, ushort loops, WebpAnimationOptions? options, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(frames);
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentNullException.ThrowIfNull(encoding);
            encoding.Validate();
            if (!destination.CanWrite || !destination.CanSeek)
            {
                throw new ArgumentException("Animated WebP requires a writable, seekable destination.", nameof(destination));
            }
            token.ThrowIfCancellationRequested();
            long start = destination.Position;
            long written = 0;
            int width = options?.CanvasWidth ?? 0;
            int height = options?.CanvasHeight ?? 0;
            bool alpha = false;
            int headerLength = checked(HeaderBytes + WebpMetadataWriter.ChunkProfileLength(encoding.Metadata));
            using WritableMediaBuffer header = allocator.Allocate(headerLength);
            await foreach (WebpAnimationFrame frame in frames.WithCancellation(token).ConfigureAwait(false))
            {
                ArgumentNullException.ThrowIfNull(frame);
                WebpEncoder.ValidateInput(frame.Image, token);
                int duration = DurationMilliseconds(frame.Image.Duration);
                using VideoFrame source = frame.Image.Retain();
                if (written == 0 && options is null) { width = source.Format.Width; height = source.Format.Height; }
                ValidateFrame(frame, width, height, options is null);
                byte flags = FrameFlags(frame);
                WebpEncodingOptions frameEncoding = frame.Encoding ?? encoding;
                frameEncoding.Validate();
                using WebpFramePayload image = WebpFramePayload.Create(source, allocator, frameEncoding, token);
                alpha |= image.HasAlpha;
                if (written == 0)
                {
                    WriteHeader(header.Memory.Span, width, height, loops, options?.BackgroundArgb ?? 0, encoding.Metadata);
                    await destination.WriteAsync(header.Memory, token).ConfigureAwait(false);
                    written = headerLength;
                }
                int length = checked(FrameHeaderBytes + image.Length);
                EnsureLength(checked(written + length + WebpMetadataWriter.DescriptionsLength(encoding.Metadata)));
                using WritableMediaBuffer output = allocator.Allocate(length);
                WriteFrameHeader(output.Memory.Span, frame, duration, image.Length, flags);
                image.Write(output.Memory[FrameHeaderBytes..], token);
                await destination.WriteAsync(output.Memory, token).ConfigureAwait(false);
                written += length;
            }
            if (written == 0) { throw new ArgumentException("Animated WebP requires at least one frame.", nameof(frames)); }
            int trailingLength = WebpMetadataWriter.DescriptionsLength(encoding.Metadata);
            if (trailingLength != 0)
            {
                using WritableMediaBuffer descriptions = allocator.Allocate(trailingLength);
                WebpMetadataWriter.WriteDescriptions(descriptions.Memory.Span, encoding.Metadata);
                await destination.WriteAsync(descriptions.Memory, token).ConfigureAwait(false);
                written += trailingLength;
            }
            WebpRiff.WriteHeader(header.Memory.Span, written);
            if (alpha) { header.Memory.Span[20] |= 16; }
            destination.Position = start;
            await destination.WriteAsync(header.Memory, token).ConfigureAwait(false);
            destination.Position = checked(start + written);
            return written;
        }

        private static void ValidateFrame(WebpAnimationFrame frame, int width, int height, bool fullCanvas)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(frame.X);
            ArgumentOutOfRangeException.ThrowIfNegative(frame.Y);
            if (((frame.X | frame.Y) & 1) != 0) { throw new ArgumentException("WebP animation frame coordinates must be even.", nameof(frame)); }
            if ((long)frame.X + frame.Image.Format.Width > width || (long)frame.Y + frame.Image.Format.Height > height)
            {
                throw new ArgumentException("WebP animation frame rectangle exceeds the canvas.", nameof(frame));
            }
            if (fullCanvas && (frame.Image.Format.Width != width || frame.Image.Format.Height != height))
            {
                throw new ArgumentException("Animated WebP frames must share the same canvas dimensions.", nameof(frame));
            }
            if (frame.Encoding is not null && WebpMetadataWriter.Flags(frame.Encoding.Metadata) != 0)
            {
                throw new ArgumentException("WebP animation metadata must be set on the animation options.", nameof(frame));
            }
        }

        private static void WriteHeader(Span<byte> header, int width, int height, ushort loops, uint background, WebpMetadata? metadata)
        {
            header.Clear();
            WebpRiff.WriteHeader(header, HeaderBytes);
            WebpRiff.WriteCanvas(header[12..], width, height, (byte)(2 | WebpMetadataWriter.Flags(metadata)));
            int position = 30 + WebpMetadataWriter.WriteProfile(header[30..], metadata);
            WebpRiff.WriteChunk(header[position..], "ANIM"u8, 6);
            BinaryPrimitives.WriteUInt32LittleEndian(header[(position + 8)..], background);
            BinaryPrimitives.WriteUInt16LittleEndian(header[(position + 12)..], loops);
        }

        private static void WriteFrameHeader(Span<byte> output, WebpAnimationFrame frame, int duration, int payloadLength, byte flags)
        {
            output[..FrameHeaderBytes].Clear();
            WebpRiff.WriteChunk(output, "ANMF"u8, checked(16 + payloadLength));
            WebpRiff.Write24(output[8..], frame.X / 2);
            WebpRiff.Write24(output[11..], frame.Y / 2);
            WebpRiff.Write24(output[14..], frame.Image.Format.Width - 1);
            WebpRiff.Write24(output[17..], frame.Image.Format.Height - 1);
            WebpRiff.Write24(output[20..], duration);
            output[23] = flags;
        }

        private static byte FrameFlags(WebpAnimationFrame frame)
        {
            byte blend = frame.Blending switch
            {
                WebpFrameBlending.Replace => 2,
                WebpFrameBlending.AlphaBlend => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(frame))
            };
            byte disposal = frame.Disposal switch
            {
                WebpFrameDisposal.Keep => 0,
                WebpFrameDisposal.Background => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(frame))
            };
            return (byte)(blend | disposal);
        }

        private static int DurationMilliseconds(MediaDuration? duration)
        {
            if (duration is not MediaDuration declared) { throw new ArgumentException("Animated WebP frames require a declared duration.", nameof(duration)); }
            Int128 milliseconds = (Int128)declared.Value * declared.TimeBase.Numerator * 1000;
            Int128 value = milliseconds / declared.TimeBase.Denominator;
            if (milliseconds % declared.TimeBase.Denominator != 0 || value > 0xffffff)
            {
                throw new NotSupportedException("WebP frame durations must be exact integer milliseconds in the 24-bit range.");
            }
            return (int)value;
        }

        private static void EnsureLength(long length)
        {
            if (length > uint.MaxValue - 1) { throw new NotSupportedException("Animated WebP exceeds the RIFF file size range."); }
        }

        private static async IAsyncEnumerable<WebpAnimationFrame> Wrap(IAsyncEnumerable<VideoFrame> frames,
            [EnumeratorCancellation] CancellationToken token)
        {
            await foreach (VideoFrame frame in frames.WithCancellation(token).ConfigureAwait(false)) { yield return new(frame); }
        }
    }
}
