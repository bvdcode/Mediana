// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual.Tests
{
    public class ConfigurationTests
    {
        [Theory]
        [InlineData("simple", 96, 80, false, false)]
        [InlineData("bframes", 96, 80, false, false)]
        [InlineData("quarter", 128, 96, true, false)]
        [InlineData("matrix", 96, 80, false, true)]
        public void IndependentLayerHeaders(string name, int width, int height, bool quarter, bool matrix)
        {
            byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name + ".m4v"));
            Mpeg4VisualConfiguration config = Mpeg4VisualConfiguration.Read(data);
            Assert.Equal(width, config.Width); Assert.Equal(height, config.Height);
            Assert.Equal(quarter, config.QuarterPel); Assert.Equal(matrix, config.MpegQuantization);
            Assert.True(config.TimeIncrementResolution > 0); Assert.Equal(1, config.AspectNumerator);
            Assert.Equal(1, config.AspectDenominator);
            int layer = 0;
            while (data[layer + 3] is < 0x20 or > 0x2f) { layer = Mpeg4VisualStartCodes.Find(data, layer + 4); }
            int end = Mpeg4VisualStartCodes.Find(data, layer + 4);
            byte[] header = data.AsSpan(layer, end - layer).ToArray();
            Assert.Equal(width, Mpeg4VisualConfiguration.Read(header).Width);
            for (int length = 0; length < header.Length - 1; length++)
            { Assert.Throws<InvalidDataException>(() => Mpeg4VisualConfiguration.Read(header.AsSpan(0, length))); }
        }
        [Fact]
        public void BitReaderChecksEveryAlignmentAndTruncation()
        {
            byte[] data = [0xc5, 0x7b, 0x92, 0xa1, 0xfe];
            for (int start = 0; start < 8; start++)
            {
                for (int count = 0; count <= 24; count++)
                {
                    Mpeg4VisualBitReader bits = new(data); bits.Skip(start); int expected = 0;
                    for (int i = start; i < start + count; i++) { expected = expected * 2 + ((data[i / 8] >> (7 - i % 8)) & 1); }
                    Assert.Equal(expected, bits.Peek(count)); Assert.Equal(start, bits.BitPosition);
                    Assert.Equal(expected, bits.Read(count)); Assert.Equal(start + count, bits.BitPosition);
                }
            }
            Assert.Throws<InvalidDataException>(ReadPastEnd);
        }
        private static void ReadPastEnd() { Mpeg4VisualBitReader bits = new(new byte[1]); bits.Read(9); }
    }
}
