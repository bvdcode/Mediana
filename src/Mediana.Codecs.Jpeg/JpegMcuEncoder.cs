// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Core.Video;

namespace Mediana.Codecs.Jpeg
{
    internal class JpegMcuEncoder(JpegOutput output, int horizontal, int vertical, bool monochrome, JpegBackgroundColor? background)
    {
        private int previousY;
        private int previousCb;
        private int previousCr;

        internal void Write(VideoFrame input, int left, int top, ReadOnlySpan<float> quantization)
        {
            Span<float> samples = stackalloc float[384];
            int lumaBlocks = horizontal * vertical;
            Span<float> cb = samples.Slice(lumaBlocks * 64, 64);
            Span<float> cr = samples.Slice((lumaBlocks + 1) * 64, 64);
            JpegSampleConverter.Fill(input, samples, left, top, horizontal, vertical, monochrome, background);
            for (int block = 0; block < lumaBlocks; block++)
            {
                previousY = WriteBlock(samples.Slice(block * 64, 64), quantization[..64], previousY, JpegEncodingTables.LumaDc, JpegEncodingTables.LumaAc);
            }
            if (!monochrome)
            {
                previousCb = WriteBlock(cb, quantization[64..], previousCb, JpegEncodingTables.ChromaDc, JpegEncodingTables.ChromaAc);
                previousCr = WriteBlock(cr, quantization[64..], previousCr, JpegEncodingTables.ChromaDc, JpegEncodingTables.ChromaAc);
            }
        }

        private int WriteBlock(ReadOnlySpan<float> samples, ReadOnlySpan<float> quantization, int previousDc,
            ReadOnlySpan<uint> dcTable, ReadOnlySpan<uint> acTable)
        {
            Span<int> coefficients = stackalloc int[64];
            JpegForwardTransform.Quantize(samples, quantization, coefficients);
            int dc = coefficients[0];
            int delta = dc - previousDc;
            int category = Category(delta);
            Symbol(dcTable[category], delta, category);
            int zeros = 0;
            for (int i = 1; i < 64; i++)
            {
                int value = coefficients[JpegTransform.Zigzag[i]];
                if (value == 0) { zeros++; continue; }
                while (zeros >= 16) { Huffman(acTable[240]); zeros -= 16; }
                category = Category(value);
                Symbol(acTable[(zeros << 4) | category], value, category);
                zeros = 0;
            }
            if (zeros != 0) { Huffman(acTable[0]); }
            return dc;
        }

        private static int Category(int value) => value == 0 ? 0 : BitOperations.Log2((uint)Math.Abs(value)) + 1;
        private void Huffman(uint entry) => output.Bits(entry & 65535, (int)(entry >> 16));
        private void Symbol(uint entry, int value, int length)
        {
            uint amplitude = unchecked((uint)(value < 0 ? value - 1 : value)) & ((1u << length) - 1);
            output.Bits(((entry & 65535) << length) | amplitude, (int)(entry >> 16) + length);
        }
    }
}
