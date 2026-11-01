// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal static class JpegProgressiveTestInput
    {
        internal static async Task VerifyOrderingAsync()
        {
            byte[] valid = Image();
            List<byte[]> invalid =
            [
                Scan(valid, 0, 0, 0, 127),
                ChangeHeader(valid, 1, 63, 0),
                ChangeHeader(valid, 0, 0, 16),
                ChangeHeader(valid, 0, 63, 0),
                ChangeHeader(valid, 4, 3, 0),
                ChangeHeader(valid, 1, 64, 0),
                ChangeHeader(valid, 0, 0, 14),
                Scan(ChangeHeader(valid, 0, 0, 2), 0, 0, 16, 127),
                Scan(ChangeHeader(valid, 0, 0, 2), 0, 0, 32, 127),
                Scan(Scan(valid, 1, 5, 0, 127), 5, 10, 0, 127),
                Scan(Scan(valid, 1, 5, 1, 127), 1, 6, 16, 127),
                Scan(ChangeHeader(valid, 0, 0, 13), 0, 0, 220, 255, 0),
            ];
            byte[] changedQuantization = new byte[65];
            changedQuantization.AsSpan(1).Fill(2);
            invalid.Add(Append(valid, JpegTestData.Segment(219, changedQuantization)));
            foreach (byte[] encoded in invalid)
            {
                await JpegDecodeTestInput.RejectAsync<InvalidDataException>(encoded);
            }
            byte[] exactQuantization = new byte[65];
            exactQuantization.AsSpan(1).Fill(1);
            await DecodeKnownGrayAsync(Append(valid, JpegTestData.Segment(219, exactQuantization)), 1024);
            await DecodeKnownGrayAsync(Scan(ChangeHeader(valid, 0, 0, 1), 0, 0, 16, 255, 0), 320);
        }

        internal static async Task VerifyEntropyAsync(string root)
        {
            byte[] valid = Image();
            List<byte[]> invalid =
            [
                Scan(ChangeAcSymbol(valid, 16), 1, 63, 0, 63),
                Scan(ChangeAcSymbol(valid, 240), 1, 15, 0, 127),
                Scan(ChangeAcSymbol(valid, 11), 1, 63, 0, 127),
                Scan(ChangeAcSymbol(valid, 1), 1, 63, 13, 127),
            ];
            byte[] badDc = valid.ToArray();
            badDc[JpegTestData.Find(badDc, 196) + 21] = 12;
            invalid.Add(badDc);
            byte[] padding = valid.ToArray();
            padding[^3] = 0;
            invalid.Add(padding);
            foreach (byte symbol in new byte[] { 16, 240, 2 })
            {
                byte[] initial = Scan(valid, 1, 15, 1, 127);
                byte[] table = new byte[18];
                table[0] = 16;
                table[1] = 1;
                table[^1] = symbol;
                initial = Append(initial, JpegTestData.Segment(196, table));
                invalid.Add(Scan(initial, 1, 15, 16, 63));
            }
            byte[] crossingRestart = ChangeAcSymbol(valid, 16);
            int frame = JpegTestData.Find(crossingRestart, 194);
            crossingRestart[frame + 8] = 17;
            crossingRestart[^3] = 31;
            crossingRestart = Append(crossingRestart, JpegTestData.Segment(221, [0, 2]));
            invalid.Add(Scan(crossingRestart, 1, 63, 0, 127));
            byte[] restart = await File.ReadAllBytesAsync(Path.Combine(root, "progressive-rgb-s2-73x35.jpg"));
            restart[JpegTestData.Find(restart, 208) + 1] = 209;
            invalid.Add(restart);
            foreach (byte[] encoded in invalid)
            {
                await JpegDecodeTestInput.RejectAsync<InvalidDataException>(encoded);
            }
            byte[] complete = await File.ReadAllBytesAsync(Path.Combine(root, "progressive-flat.jpg"));
            for (int length = 0; length < complete.Length; length++)
            {
                await JpegDecodeTestInput.RejectAsync<InvalidDataException>(complete[..length]);
            }
        }

        internal static async Task VerifyResourcesAsync()
        {
            byte[] encoded = Image();
            foreach (int budget in new int[] { 255, 256, 319 })
            {
                MediaBufferAllocator allocator = new(budget);
                bool rejected = false;
                try
                {
                    using VideoFrame unexpected = await new JpegImageDecoder().DecodeAsync(encoded, new(), allocator);
                }
                catch (MediaMemoryLimitException)
                {
                    rejected = true;
                }
                if (!rejected || allocator.AllocatedBytes != 0)
                {
                    throw new InvalidDataException("Progressive JPEG must account for and release coefficients and samples at every allocation boundary.");
                }
            }
            await DecodeKnownGrayAsync(encoded, 320);
            await JpegDecodeTestInput.RejectAsync<InvalidDataException>(encoded, new() { MaximumMarkers = 5 });
        }

        private static byte[] Image()
        {
            byte[] encoded = JpegTestData.Image();
            encoded[JpegTestData.Find(encoded, 192) + 1] = 194;
            encoded[JpegTestData.Find(encoded, 218) + 8] = 0;
            encoded[^3] = 127;
            return encoded;
        }

        private static byte[] ChangeHeader(byte[] source, byte first, byte last, byte approximation)
        {
            byte[] encoded = source.ToArray();
            int scan = JpegTestData.Find(encoded, 218);
            encoded[scan + 7] = first;
            encoded[scan + 8] = last;
            encoded[scan + 9] = approximation;
            return encoded;
        }

        private static byte[] ChangeAcSymbol(byte[] source, byte symbol)
        {
            byte[] encoded = source.ToArray();
            encoded[JpegTestData.Find(encoded, 196) + 39] = symbol;
            return encoded;
        }

        private static byte[] Scan(byte[] source, byte first, byte last, byte approximation, params byte[] entropy) =>
            Append(source, [.. JpegTestData.Segment(218, [1, 1, 0, first, last, approximation]), .. entropy]);

        private static byte[] Append(byte[] source, ReadOnlySpan<byte> data) =>
            [.. source.AsSpan(0, source.Length - 2), .. data, 255, 217];

        private static async Task DecodeKnownGrayAsync(byte[] encoded, int budget)
        {
            MediaBufferAllocator allocator = new(budget);
            using (VideoFrame frame = await new JpegImageDecoder().DecodeAsync(encoded, new(), allocator))
            {
                if (allocator.AllocatedBytes != 4 || !frame.Data.Span.SequenceEqual(new byte[] { 128, 128, 128, 255 }))
                {
                    throw new InvalidDataException("Progressive JPEG must retain only its independent final pixels.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Progressive JPEG retained storage after frame disposal.");
            }
        }
    }
}
