// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;
using System.Buffers.Binary;
using Mediana.Codecs.Webp;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.NativeTests
{
    internal static class NativeWebpTests
    {
        internal static async Task RunAsync()
        {
            foreach ((int width, int height) in new[] { (1, 1), (1, 65), (65, 1), (33, 35), (129, 101) })
            {
                for (int pattern = 0; pattern < 4; pattern++) { WebpEncodingTestInput.Verify(width, height, pattern, true); }
            }
            await WebpEncodingTestInput.VerifyStreamingAsync();
            await WebpEncodingTestInput.VerifyAnimationAsync();
            VerifyLossy();
            Console.WriteLine("PASS WebP: exact VP8L RGBA, strides, transforms, LZ77, async output, animation timing, VP8 headers and compressed alpha ownership.");
        }

        private static void VerifyLossy()
        {
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            foreach ((int width, int height) in new[] { (1, 65), (65, 1), (33, 35) })
            {
                using VideoFrame frame = WebpEncodingTestInput.Frame(WebpEncodingTestInput.Pixels(width, height, 1), width, height, allocator, true);
                long initial = allocator.AllocatedBytes;
                foreach (int quality in new[] { 0, 75, 100 })
                {
                    using MediaBuffer output = WebpEncoder.Encode(frame, allocator, new WebpEncodingOptions { Compression = WebpCompression.Lossy, Quality = quality });
                    ReadOnlySpan<byte> file = output.Memory.Span;
                    if (!file[..4].SequenceEqual("RIFF"u8) || BinaryPrimitives.ReadUInt32LittleEndian(file[4..]) != file.Length - 8
                        || allocator.AllocatedBytes != initial + file.Length)
                    {
                        throw new InvalidDataException("VP8 encoding changed RIFF lengths or retained temporary workspace.");
                    }
                    int position = 30;
                    if (!file.Slice(position, 4).SequenceEqual("ALPH"u8)) { throw new InvalidDataException("VP8 encoding omitted alpha."); }
                    int alphaLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(file[(position + 4)..]));
                    position += 8 + alphaLength + (alphaLength & 1);
                    if (!file.Slice(position, 4).SequenceEqual("VP8 "u8)) { throw new InvalidDataException("VP8 encoding omitted its image chunk."); }
                    ReadOnlyMemory<byte> payload = output.Memory[(position + 8)..];
                    int tag = payload.Span[0] | payload.Span[1] << 8 | payload.Span[2] << 16;
                    Vp8TestBooleanReader reader = new(payload.Slice(10, tag >> 5));
                    if (reader.Literal(4) != 0 || reader.Literal(6) != 0 || reader.Literal(3) != 0 || reader.Read()
                        || reader.Literal(2) != 0 || reader.Literal(7) != (quality switch
                        {
                            0 => 127,
                            75 => 26,
                            100 => 0,
                            _ => throw new ArgumentOutOfRangeException(nameof(quality))
                        }))
                    {
                        throw new InvalidDataException("VP8 frame quantization header differs from requested quality.");
                    }
                }
            }
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("VP8 encoding retained source or output storage."); }
        }
    }
}
