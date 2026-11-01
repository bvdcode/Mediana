// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg
{
    internal static class JpegSequentialScan
    {
        internal static void Read(ReadOnlySpan<byte> header, JpegMarkerReader reader, JpegDocument image,
            bool decode, int restartInterval, CancellationToken token)
        {
            if (header.Length < 4 || header[0] is < 1 or > 3 || header.Length != 4 + header[0] * 2 || image.Components.Count == 0)
            {
                throw new InvalidDataException("JPEG scan header is invalid or precedes its frame.");
            }
            int count = header[0];
            if (header[^3] != 0 || header[^2] != 63 || header[^1] != 0)
            {
                throw new InvalidDataException("Sequential JPEG scans require the complete coefficient band without approximation.");
            }
            Span<int> indices = stackalloc int[3];
            Span<int> dcTables = stackalloc int[3];
            Span<int> acTables = stackalloc int[3];
            int blocks = 0;
            for (int i = 0; i < count; i++)
            {
                int componentIndex = -1;
                for (int j = 0; j < image.Components.Count; j++)
                {
                    if (image.Components[j].Id == header[1 + i * 2])
                    {
                        componentIndex = j;
                    }
                }
                if (componentIndex < 0 || image.Components[componentIndex].Decoded || indices[..i].Contains(componentIndex))
                {
                    throw new InvalidDataException("JPEG scan selects an unknown, repeated or already decoded component.");
                }
                JpegComponent component = image.Components[componentIndex];
                indices[i] = componentIndex;
                int dc = header[2 + i * 2] >> 4;
                int ac = header[2 + i * 2] & 15;
                if (dc > 3 || ac > 3)
                {
                    throw new InvalidDataException("JPEG scan Huffman table selectors are invalid.");
                }
                dcTables[i] = dc;
                acTables[i] = ac + 4;
                image.GetHuffman(dc);
                image.GetHuffman(ac + 4);
                image.GetQuantization(component);
                blocks += component.Horizontal * component.Vertical;
            }
            if (count > 1 && blocks > 10)
            {
                throw new InvalidDataException("JPEG interleaved scan exceeds ten blocks per MCU.");
            }
            if (decode)
            {
                Decode(reader, image, indices[..count], dcTables, acTables, restartInterval, token);
            }
            else
            {
                reader.SkipEntropy();
            }
            foreach (int index in indices[..count])
            {
                image.Components[index].Decoded = true;
            }
        }

        private static void Decode(JpegMarkerReader reader, JpegDocument image, ReadOnlySpan<int> indices,
            ReadOnlySpan<int> dcTables, ReadOnlySpan<int> acTables, int interval, CancellationToken token)
        {
            bool interleaved = indices.Length > 1;
            JpegComponent first = image.Components[indices[0]];
            int columns;
            int rows;
            if (interleaved)
            {
                columns = (image.Width + image.MaximumHorizontal * 8 - 1) / (image.MaximumHorizontal * 8);
                rows = (image.Height + image.MaximumVertical * 8 - 1) / (image.MaximumVertical * 8);
            }
            else
            {
                columns = (first.Width + 7) / 8;
                rows = (first.Height + 7) / 8;
            }
            JpegEntropyReader bits = new(reader.Encoded.Span, reader.Position);
            Span<int> predictors = stackalloc int[3];
            predictors.Clear();
            Span<int> coefficients = stackalloc int[64];
            int restart = 0;
            int total = checked(columns * rows);
            for (int mcu = 0; mcu < total; mcu++)
            {
                token.ThrowIfCancellationRequested();
                if (interval != 0 && mcu != 0 && mcu % interval == 0)
                {
                    bits.Align();
                    reader.Position = bits.Position;
                    if (reader.NextMarker() != 208 + restart)
                    {
                        throw new InvalidDataException("JPEG restart marker is missing or out of sequence.");
                    }
                    restart = (restart + 1) & 7;
                    predictors.Clear();
                    bits = new(reader.Encoded.Span, reader.Position);
                }
                for (int i = 0; i < indices.Length; i++)
                {
                    JpegComponent component = image.Components[indices[i]];
                    int horizontal = 1;
                    int vertical = 1;
                    if (interleaved)
                    {
                        horizontal = component.Horizontal;
                        vertical = component.Vertical;
                    }
                    int[] quantization = image.GetQuantization(component);
                    JpegHuffmanTable dc = image.GetHuffman(dcTables[i]);
                    JpegHuffmanTable ac = image.GetHuffman(acTables[i]);
                    for (int y = 0; y < vertical; y++)
                    {
                        for (int x = 0; x < horizontal; x++)
                        {
                            coefficients.Clear();
                            DecodeBlock(ref bits, dc, ac, ref predictors[i], coefficients);
                            int blockX = (mcu % columns) * horizontal + x;
                            int blockY = (mcu / columns) * vertical + y;
                            Span<byte> output = component.Samples!.Memory.Span[(blockY * 8 * component.Stride + blockX * 8)..];
                            JpegInverseTransform.Write(coefficients, quantization, output, component.Stride);
                        }
                    }
                }
            }
            bits.Align();
            reader.Position = bits.Position;
        }

        private static void DecodeBlock(ref JpegEntropyReader bits, JpegHuffmanTable dc, JpegHuffmanTable ac,
            ref int predictor, scoped Span<int> coefficients)
        {
            int category = dc.Decode(ref bits);
            if (category > 11)
            {
                throw new InvalidDataException("JPEG 8-bit DC difference category exceeds eleven bits.");
            }
            predictor += bits.Receive(category);
            if (predictor is < -2048 or > 2047)
            {
                throw new InvalidDataException("JPEG DC predictor exceeds the supported coefficient range.");
            }
            coefficients[0] = predictor;
            int position = 1;
            while (position < 64)
            {
                int symbol = ac.Decode(ref bits);
                int run = symbol >> 4;
                int size = symbol & 15;
                if (size == 0)
                {
                    if (run == 0)
                    {
                        return;
                    }
                    if (run != 15 || position + 16 > 64)
                    {
                        throw new InvalidDataException("JPEG AC zero run is invalid or exceeds the coefficient block.");
                    }
                    position += 16;
                }
                else
                {
                    position += run;
                    if (size > 10 || position >= 64)
                    {
                        throw new InvalidDataException("JPEG AC coefficient size or zero run is invalid.");
                    }
                    coefficients[JpegInverseTransform.Zigzag[position++]] = bits.Receive(size);
                }
            }
        }
    }
}
