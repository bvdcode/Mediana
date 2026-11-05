// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Codecs.Webp
{
    /// <summary>Writes full-canvas lossless WebP frames in display order without blending between frames.</summary>
    public static class WebpAnimationEncoder
    {
        private const int HeaderBytes = 44;
        private const int FrameHeaderBytes = 32;

        /// <summary>Encodes borrowed frames incrementally. Each frame needs an exact integer-millisecond duration
        /// and identical square-pixel sRGB RGBA dimensions. Loop count zero means infinite repetition.
        /// The writable destination must seek so the RIFF size and alpha flag can be completed after enumeration.
        /// Only one frame's compression workspace is live at once. Leaves destination open and positioned after the file.
        /// Failure leaves incomplete output that must be discarded; the caller owns yielded frames.</summary>
        public static async Task<long> EncodeAsync(IAsyncEnumerable<VideoFrame> frames, Stream destination, MediaBufferAllocator allocator,
            ushort loopCount = 0, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(frames);
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(allocator);
            if (!destination.CanWrite || !destination.CanSeek)
            {
                throw new ArgumentException("Animated WebP requires a writable, seekable destination.", nameof(destination));
            }
            cancellationToken.ThrowIfCancellationRequested();
            long start = destination.Position;
            long written = 0;
            int width = 0;
            int height = 0;
            bool alpha = false;
            using WritableMediaBuffer header = allocator.Allocate(HeaderBytes);
            await foreach (VideoFrame input in frames.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                WebpEncoder.ValidateInput(input, cancellationToken);
                int duration = DurationMilliseconds(input.Duration);
                using VideoFrame source = input.Retain();
                if (written == 0)
                {
                    width = source.Format.Width;
                    height = source.Format.Height;
                    WriteHeader(header.Memory.Span, width, height, loopCount);
                    await destination.WriteAsync(header.Memory, cancellationToken).ConfigureAwait(false);
                    written = HeaderBytes;
                }
                else if (source.Format.Width != width || source.Format.Height != height)
                {
                    throw new ArgumentException("Animated WebP frames must share the same canvas dimensions.", nameof(frames));
                }
                using WebpLosslessImage image = WebpLosslessImage.Create(source, allocator, cancellationToken);
                alpha |= image.HasAlpha;
                int length = checked(FrameHeaderBytes + image.PayloadLength + (image.PayloadLength & 1));
                if (written + length - 8 > uint.MaxValue - 1)
                {
                    throw new NotSupportedException("Animated WebP exceeds the RIFF file size range.");
                }
                using WritableMediaBuffer frame = allocator.Allocate(length);
                WriteFrameHeader(frame.Memory.Span, width, height, duration, image.PayloadLength);
                image.Write(frame.Memory.Slice(FrameHeaderBytes, image.PayloadLength), cancellationToken);
                await destination.WriteAsync(frame.Memory, cancellationToken).ConfigureAwait(false);
                written += length;
            }
            if (written == 0) { throw new ArgumentException("Animated WebP requires at least one frame.", nameof(frames)); }
            BinaryPrimitives.WriteUInt32LittleEndian(header.Memory.Span[4..], checked((uint)(written - 8)));
            if (alpha) { header.Memory.Span[20] |= 16; }
            destination.Position = start;
            await destination.WriteAsync(header.Memory, cancellationToken).ConfigureAwait(false);
            destination.Position = checked(start + written);
            return written;
        }

        private static void WriteHeader(Span<byte> header, int width, int height, ushort loops)
        {
            header.Clear();
            "RIFF"u8.CopyTo(header);
            "WEBPVP8X"u8.CopyTo(header[8..]);
            BinaryPrimitives.WriteUInt32LittleEndian(header[16..], 10);
            header[20] = 2;
            Write24(header[24..], width - 1);
            Write24(header[27..], height - 1);
            "ANIM"u8.CopyTo(header[30..]);
            BinaryPrimitives.WriteUInt32LittleEndian(header[34..], 6);
            BinaryPrimitives.WriteUInt16LittleEndian(header[42..], loops);
        }

        private static void WriteFrameHeader(Span<byte> header, int width, int height, int duration, int payloadLength)
        {
            header[..FrameHeaderBytes].Clear();
            "ANMF"u8.CopyTo(header);
            BinaryPrimitives.WriteUInt32LittleEndian(header[4..], checked((uint)(24 + payloadLength + (payloadLength & 1))));
            Write24(header[14..], width - 1);
            Write24(header[17..], height - 1);
            Write24(header[20..], duration);
            header[23] = 2;
            "VP8L"u8.CopyTo(header[24..]);
            BinaryPrimitives.WriteUInt32LittleEndian(header[28..], (uint)payloadLength);
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

        private static void Write24(Span<byte> output, int value)
        {
            output[0] = (byte)value;
            output[1] = (byte)(value >> 8);
            output[2] = (byte)(value >> 16);
        }
    }
}
