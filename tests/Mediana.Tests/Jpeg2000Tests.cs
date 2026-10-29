// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.Jpeg2000;

namespace Mediana.Tests
{
    public class Jpeg2000Tests
    {
        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg2000");

        [Fact]
        public async Task AllSeventyIndependentReferencesMatch()
        {
            await Jpeg2000ReferenceTestInput.VerifyAllAsync(Root);
        }

        [Fact]
        public async Task DecoderLimitsAndCancellationAreEnforced()
        {
            byte[] data = await File.ReadAllBytesAsync(Path.Combine(Root, "tiles.jp2"));
            Jpeg2000DecodeOptions[] options =
            [
                new() { MaximumEncodedBytes = data.Length - 1 },
                new() { MaximumPixels = 10 },
                new() { MaximumWorkingBytes = 128 },
                new() { MaximumPackets = 30 },
                new() { ExpectedWidth = 1 },
                new() { ExpectedHeight = 1 }
            ];
            foreach (Jpeg2000DecodeOptions option in options)
            {
                Assert.Throws<InvalidDataException>(() => Jpeg2000Decoder.Decode(data, option));
            }
            Assert.Throws<OperationCanceledException>(() => Jpeg2000Decoder.Decode(data, cancellationToken: new CancellationToken(true)));
        }

        [Theory]
        [InlineData(0x5f)]
        [InlineData(0x60)]
        [InlineData(0x61)]
        public async Task UnsupportedPacketHeadersAreRejected(int marker)
        {
            byte[] data = await File.ReadAllBytesAsync(Path.Combine(Root, "raw.j2k"));
            int start = 4 + BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4, 2));
            byte[] changed = [.. data[..start], 255, (byte)marker, 0, 2, .. data[start..]];
            Assert.Throws<NotSupportedException>(() => Jpeg2000Decoder.Decode(changed));
        }

        [Fact]
        public async Task TruncationAndContainerMismatchAreRejected()
        {
            byte[] data = await File.ReadAllBytesAsync(Path.Combine(Root, "RGB-63x45.jp2"));
            foreach (int length in new[] { 0, 1, 7, 12, data.Length / 2, data.Length - 1 })
            {
                Assert.Throws<InvalidDataException>(() => Jpeg2000Decoder.Decode(data.AsMemory(0, length)));
            }
            int header = data.AsSpan().IndexOf("ihdr"u8);
            data[header + 7] ^= 1;
            Assert.Throws<InvalidDataException>(() => Jpeg2000Decoder.Decode(data));
        }

        [Fact]
        public async Task ParallelDecodersDoNotShareMutableState()
        {
            byte[] data = await File.ReadAllBytesAsync(Path.Combine(Root, "style-63.j2k"));
            int[][] samples = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
                Jpeg2000Decoder.Decode(data).GetComponent(0).Samples.ToArray())));
            foreach (int[] sample in samples)
            {
                Assert.Equal(samples[0], sample);
            }
        }
    }
}
