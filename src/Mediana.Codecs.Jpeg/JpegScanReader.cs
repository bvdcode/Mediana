// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg
{
    internal static class JpegScanReader
    {
        internal static void Read(ReadOnlySpan<byte> header, JpegMarkerReader reader, JpegDocument image,
            bool decode, int restartInterval, CancellationToken token)
        {
            if (header.Length < 4 || header[0] is < 1 or > 3 || header.Length != 4 + header[0] * 2 || image.Components.Count == 0)
            {
                throw new InvalidDataException("JPEG scan header is invalid or precedes its frame.");
            }
            int count = header[0];
            JpegScanParameters scan = new(header[^3], header[^2], header[^1] >> 4, header[^1] & 15);
            scan.Validate(image.Progressive, count);
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
                if (componentIndex < 0 || (i != 0 && componentIndex <= indices[i - 1]) ||
                    (!image.Progressive && image.Components[componentIndex].Decoded))
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
                if (!image.Progressive || (scan.First == 0 && scan.High == 0))
                {
                    image.GetHuffman(dc);
                }
                if (!image.Progressive || scan.First != 0)
                {
                    image.GetHuffman(ac + 4);
                }
                image.GetQuantization(component);
                if (image.Progressive)
                {
                    component.Progression!.Validate(scan);
                    image.LockQuantization(component);
                }
                blocks += component.Horizontal * component.Vertical;
            }
            if (count > 1 && blocks > 10)
            {
                throw new InvalidDataException("JPEG interleaved scan exceeds ten blocks per MCU.");
            }
            if (decode)
            {
                Decode(reader, image, indices[..count], dcTables, acTables, scan, restartInterval, token);
            }
            else
            {
                reader.SkipEntropy();
            }
            foreach (int index in indices[..count])
            {
                JpegComponent component = image.Components[index];
                component.Progression?.Complete(scan);
                if (!image.Progressive || scan.First == 0)
                {
                    component.Decoded = true;
                }
            }
        }

        private static void Decode(JpegMarkerReader reader, JpegDocument image, ReadOnlySpan<int> indices,
            ReadOnlySpan<int> dcTables, ReadOnlySpan<int> acTables, JpegScanParameters scan, int interval, CancellationToken token)
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
            int endOfBands = 0;
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
                    JpegHuffmanTable? dc = null;
                    JpegHuffmanTable? ac = null;
                    if (!image.Progressive || (scan.First == 0 && scan.High == 0))
                    {
                        dc = image.GetHuffman(dcTables[i]);
                    }
                    if (!image.Progressive || scan.First != 0)
                    {
                        ac = image.GetHuffman(acTables[i]);
                    }
                    for (int y = 0; y < vertical; y++)
                    {
                        for (int x = 0; x < horizontal; x++)
                        {
                            int blockX = (mcu % columns) * horizontal + x;
                            int blockY = (mcu / columns) * vertical + y;
                            if (image.Progressive)
                            {
                                int remaining = total - mcu;
                                if (interval != 0)
                                {
                                    remaining = Math.Min(remaining, interval - mcu % interval);
                                }
                                JpegProgressiveBlock.Decode(ref bits, dc, ac, scan, ref predictors[i], ref endOfBands,
                                    remaining, component.Progression!.Block(blockX, blockY, component.Stride));
                            }
                            else
                            {
                                coefficients.Clear();
                                JpegSequentialBlock.Decode(ref bits, dc!, ac!, ref predictors[i], coefficients);
                                Span<byte> output = component.Samples!.Memory.Span[(blockY * 8 * component.Stride + blockX * 8)..];
                                JpegInverseTransform.Write(coefficients, quantization, output, component.Stride);
                            }
                        }
                    }
                }
            }
            bits.Align();
            reader.Position = bits.Position;
        }

    }
}
