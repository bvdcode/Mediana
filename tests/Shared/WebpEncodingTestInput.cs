// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.Webp;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal static class WebpEncodingTestInput
    {
        internal static byte[] Pixels(int width, int height, int pattern)
        {
            byte[] pixels = new byte[width * height * 4];
            uint random = 0xf31a87b5;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int offset = (y * width + x) * 4;
                    for (int channel = 0; channel < 4; channel++)
                    {
                        random ^= random << 13; random ^= random >> 17; random ^= random << 5;
                        pixels[offset + channel] = pattern switch
                        {
                            0 => (byte)(channel * 71),
                            1 => (byte)(x * 7 + y * 11 + channel * 19),
                            2 => (byte)random,
                            3 => (byte)((x / 7 + y / 13) % 5 * (channel + 3) * 37),
                            _ => throw new ArgumentOutOfRangeException(nameof(pattern))
                        };
                    }
                }
            }
            return pixels;
        }

        internal static VideoFrame Frame(byte[] rgba, int width, int height, MediaBufferAllocator allocator,
            bool padded = false, MediaDuration? duration = null)
        {
            int offset = padded ? 7 : 0;
            int stride = width * 4 + (padded ? 11 : 0);
            using WritableMediaBuffer writable = allocator.Allocate(offset + stride * height);
            writable.Memory.Span.Fill(0xa5);
            for (int row = 0; row < height; row++) { rgba.AsSpan(row * width * 4, width * 4).CopyTo(writable.Memory.Span.Slice(offset + row * stride, width * 4)); }
            using MediaBuffer buffer = writable.Publish();
            VideoFormat format = new(width, height, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, SampleAspectRatio.Square);
            return new(format, buffer, [new(offset, stride, width * 4, height)], duration: duration);
        }

        internal static void Verify(int width, int height, int pattern, bool padded)
        {
            byte[] pixels = Pixels(width, height, pattern);
            MediaBufferAllocator allocator = new(64 * 1024 * 1024);
            using (VideoFrame frame = Frame(pixels, width, height, allocator, padded))
            {
                long inputBytes = allocator.AllocatedBytes;
                byte[] unchanged = frame.Data.ToArray();
                using (MediaBuffer output = WebpEncoder.Encode(frame, allocator))
                {
                    byte[] actual = WebpLosslessTestReader.Decode(output.Memory, out int actualWidth, out int actualHeight);
                    if (actualWidth != width || actualHeight != height || !actual.AsSpan().SequenceEqual(pixels)
                        || !unchanged.AsSpan().SequenceEqual(frame.Data.Span) || allocator.AllocatedBytes != inputBytes + output.Memory.Length)
                    {
                        throw new InvalidDataException("WebP encoding changed RGBA, source guards or retained compression workspace.");
                    }
                }
                if (allocator.AllocatedBytes != inputBytes) { throw new InvalidDataException("WebP output storage was not released."); }
            }
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("WebP source storage was not released."); }
        }

        internal static async Task VerifyStreamingAsync()
        {
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            byte[] pixels = Pixels(129, 101, 2);
            using VideoFrame frame = Frame(pixels, 129, 101, allocator, true);
            long inputBytes = allocator.AllocatedBytes;
            using ImageOutputTestStream stream = new();
            long length = await WebpEncoder.EncodeAsync(frame, stream, allocator);
            if (!stream.CanWrite || length != stream.ToArray().Length
                || !WebpLosslessTestReader.Decode(stream.ToArray(), out _, out _).AsSpan().SequenceEqual(pixels)
                || allocator.AllocatedBytes != inputBytes)
            {
                throw new InvalidDataException("WebP async output changed ownership, byte count or RGBA.");
            }
        }

        internal static async Task VerifyAnimationAsync()
        {
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            List<VideoFrame> frames = [];
            using MemoryStream output = new();
            try
            {
                for (int pattern = 0; pattern < 4; pattern++)
                {
                    frames.Add(Frame(Pixels(33, 35, pattern), 33, 35, allocator, true, new MediaDuration(41 + pattern, new(1, 1000))));
                }
                long inputBytes = allocator.AllocatedBytes;
                output.Write(new byte[7]);
                long length = await WebpAnimationEncoder.EncodeAsync(Enumerate(frames), output, allocator, 3);
                byte[] file = output.ToArray()[7..];
                if (output.Position != 7 + length || BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4)) != length - 8
                    || (file[20] & 18) != 18 || BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(42)) != 3)
                {
                    throw new InvalidDataException("Animated WebP changed RIFF size, alpha flags, loops or stream position.");
                }
                int offset = 44;
                for (int frame = 0; frame < frames.Count; frame++)
                {
                    int size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(offset + 4)));
                    if (!file.AsSpan(offset, 4).SequenceEqual("ANMF"u8) || file[offset + 20] != 41 + frame || file[offset + 23] != 2)
                    {
                        throw new InvalidDataException("Animated WebP changed frame duration or replacement composition.");
                    }
                    int payload = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(offset + 28)));
                    byte[] still = new byte[20 + payload + (payload & 1)];
                    "RIFF"u8.CopyTo(still);
                    BinaryPrimitives.WriteUInt32LittleEndian(still.AsSpan(4), (uint)(still.Length - 8));
                    "WEBP"u8.CopyTo(still.AsSpan(8));
                    file.AsSpan(offset + 24, 8 + payload + (payload & 1)).CopyTo(still.AsSpan(12));
                    byte[] rgba = WebpLosslessTestReader.Decode(still, out _, out _);
                    if (!rgba.AsSpan().SequenceEqual(Pixels(33, 35, frame))) { throw new InvalidDataException("Animated WebP changed a frame's exact pixels."); }
                    offset += size + 8 + (size & 1);
                }
                if (offset != file.Length || allocator.AllocatedBytes != inputBytes)
                {
                    throw new InvalidDataException("Animated WebP retained workspace or wrote incorrect frame boundaries.");
                }
            }
            finally
            {
                foreach (VideoFrame frame in frames) { frame.Dispose(); }
            }
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("Animated WebP retained frame storage."); }
        }

        internal static async IAsyncEnumerable<VideoFrame> Enumerate(IEnumerable<VideoFrame> frames)
        {
            foreach (VideoFrame frame in frames) { await Task.Yield(); yield return frame; }
        }
    }
}
