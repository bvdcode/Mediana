// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.Jbig2;

namespace Mediana.Tests
{
    public class Jbig2Tests
    {
        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data", "Jbig2");

        [Fact]
        public async Task AllNinetyEightIndependentReferencesMatch()
        {
            await Jbig2ReferenceTestInput.VerifyAllAsync(Root);
        }

        [Fact]
        public async Task ResourceLimitsAndCancellationAreEnforced()
        {
            byte[] data = await File.ReadAllBytesAsync(Path.Combine(Root, "text-h0-mmr0-t0-c1-x0.jb2"));
            Jbig2DecodeOptions[] options =
            [
                new() { MaximumEncodedBytes = data.Length - 1 },
                new() { MaximumPixels = 100 },
                new() { MaximumWorkingBytes = 1024 },
                new() { MaximumDecodedPixels = 10 },
                new() { MaximumSegments = 2 },
                new() { MaximumSymbols = 2 },
                new() { MaximumInstances = 2 },
                new() { ExpectedWidth = 1 },
                new() { ExpectedHeight = 1 }
            ];
            foreach (Jbig2DecodeOptions option in options)
            {
                Assert.Throws<InvalidDataException>(() => Jbig2Decoder.Decode(data, options: option));
            }
            Assert.Throws<OperationCanceledException>(() => Jbig2Decoder.Decode(data, cancellationToken: new CancellationToken(true)));
            Assert.Throws<ArgumentOutOfRangeException>(() => Jbig2Decoder.Decode(data, options: new() { MaximumPages = 0 }));
        }

        [Theory]
        [InlineData("generic-t0-tp0-at0")]
        [InlineData("mmr-noise")]
        [InlineData("text-refined-h1-t0")]
        [InlineData("custom-huffman")]
        public async Task TruncatedSegmentsAreRejected(string name)
        {
            byte[] data = await File.ReadAllBytesAsync(Path.Combine(Root, name + ".jb2"));
            foreach (int length in new[] { 0, 1, 8, 9, 10, 20, data.Length / 2, data.Length - 1 })
            {
                Assert.Throws<InvalidDataException>(() => Jbig2Decoder.Decode(data.AsMemory(0, length)));
            }
        }

        [Fact]
        public async Task MultiplePageAssociationsAndPackedPixelsArePreserved()
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(Root, "mmr-odd.jb2"));
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(Root, "mmr-odd.bin"));
            List<byte[]> parts = Parts(input);
            byte[] data = [.. Segment(1, 48, 1, parts[0]), .. Segment(2, 38, 1, parts[1]),
                .. Segment(3, 48, 300, parts[0]), .. Segment(4, 38, 300, parts[1])];
            Jbig2Document result = Jbig2Decoder.Decode(data);
            Assert.Equal(new uint[] { 1, 300 }, result.Pages.Select(page => page.Number));
            foreach (Jbig2Page page in result.Pages)
            {
                Assert.Equal(expected, page.Data.ToArray());
                for (int y = 0; y < page.Height; y++)
                {
                    for (int x = 0; x < page.Width; x++)
                    {
                        Assert.Equal((expected[y * page.Stride + x / 8] & (128 >> (x % 8))) != 0, page.GetPixel(x, y));
                    }
                }
            }
            Assert.Throws<InvalidDataException>(() => Jbig2Decoder.Decode(data, options: new() { MaximumPages = 1 }));
        }

        [Fact]
        public async Task InvalidSegmentReferencesAreRejected()
        {
            List<byte[]> parts = Parts(await File.ReadAllBytesAsync(Path.Combine(Root, "mmr-odd.jb2")));
            byte[] page = Segment(1, 48, 1, parts[0]);
            Assert.Throws<InvalidDataException>(() => Jbig2Decoder.Decode((byte[])[.. page, .. page]));
            Assert.Throws<InvalidDataException>(() => Jbig2Decoder.Decode(Segment(1, 38, 1, parts[1])));
            Assert.Throws<InvalidDataException>(() => Jbig2Decoder.Decode((byte[])[.. page, .. Segment(3, 38, 1, parts[1], 2)]));
            Assert.Throws<InvalidDataException>(() => Jbig2Decoder.Decode((byte[])[.. page, .. Segment(3, 38, 1, parts[1], 4)]));
            Assert.Throws<NotSupportedException>(() => Jbig2Decoder.Decode((byte[])[.. page, .. Segment(2, 62, 0, [128, 0, 0, 0])]));
        }

        private static List<byte[]> Parts(byte[] input)
        {
            int offset = (input[8] & 2) != 0 ? 9 : 13;
            List<byte[]> result = [];
            while (offset < input.Length)
            {
                int flags = input[offset + 4];
                int references = input[offset + 5] >> 5;
                int header = 6 + references + ((flags & 64) == 0 ? 1 : 4) + 4;
                int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(input.AsSpan(offset + header - 4, 4)));
                result.Add(input.AsSpan(offset + header, length).ToArray());
                offset += header + length;
            }
            return result;
        }

        private static byte[] Segment(int number, int type, uint page, byte[] payload, byte? reference = null)
        {
            int references = reference.HasValue ? 1 : 0;
            byte[] result = new byte[14 + references + payload.Length];
            BinaryPrimitives.WriteUInt32BigEndian(result, (uint)number);
            result[4] = (byte)(type | 64);
            result[5] = (byte)(references << 5);
            if (reference.HasValue)
            {
                result[6] = reference.Value;
            }
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(6 + references), page);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(10 + references), (uint)payload.Length);
            payload.CopyTo(result, 14 + references);
            return result;
        }
    }
}
