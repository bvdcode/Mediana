// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Jpeg
{
    internal static class JpegLosslessScan
    {
        internal static void Decode(JpegMarkerReader reader, JpegDocument image, ReadOnlySpan<int> indices, ReadOnlySpan<int> dcTables,
            JpegScanParameters scan, int interval, MediaBufferAllocator allocator, CancellationToken token)
        {
            bool interleaved = indices.Length > 1;
            JpegComponent first = image.Components[indices[0]];
            int columns = interleaved ? (image.Width + image.MaximumHorizontal - 1) / image.MaximumHorizontal : first.Width;
            int rows = interleaved ? (image.Height + image.MaximumVertical - 1) / image.MaximumVertical : first.Height;
            if (interval % columns != 0) { throw new InvalidDataException("Lossless JPEG restart intervals must contain complete MCU rows."); }

            // One previous line plus the current MCU's lines per component, at prediction precision.
            JpegLosslessComponent[] components = new JpegLosslessComponent[indices.Length];
            int samples = 0;
            for (int i = 0; i < indices.Length; i++)
            {
                JpegComponent component = image.Components[indices[i]];
                components[i] = new(component, image.GetHuffman(dcTables[i]), interleaved, image.Precision, scan, samples);
                samples = checked(samples + components[i].HistoryLength);
            }
            using WritableMediaBuffer work = allocator.Allocate(checked(samples * sizeof(ushort)));
            Span<ushort> history = MemoryMarshal.Cast<byte, ushort>(work.Memory.Span);
            JpegEntropyReader bits = new(reader.Encoded.Span, reader.Position);
            int restart = 0, firstRow = 0;
            int restartRows = interval / columns;
            for (int row = 0; row < rows; row++)
            {
                if (restartRows != 0 && row != 0 && row % restartRows == 0)
                {
                    bits.Align();
                    reader.Position = bits.Position;
                    if (reader.NextMarker() != 208 + restart) { throw new InvalidDataException("Invalid lossless JPEG restart marker."); }
                    bits = new(reader.Encoded.Span, reader.Position);
                    restart = (restart + 1) & 7;
                    firstRow = row;
                }
                for (int column = 0; column < columns; column++)
                {
                    token.ThrowIfCancellationRequested();
                    foreach (JpegLosslessComponent component in components)
                    {
                        component.DecodeMcu(ref bits, history, row, column, firstRow);
                    }
                }
            }
            bits.Align();
            reader.Position = bits.Position;
        }
    }
}
