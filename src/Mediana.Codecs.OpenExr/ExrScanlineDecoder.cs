// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.OpenExr
{
    internal static class ExrScanlineDecoder
    {
        internal static VideoFrame Decode(ExrDocument document, int partIndex, string layer, MediaBufferAllocator allocator, CancellationToken token)
        {
            ExrHeader header = document.Parts[partIndex];
            if (header.Type != "scanlineimage" || header.Compression is < 0 or > 3)
            { throw new NotSupportedException("OpenEXR decoding supports scanline parts with NONE, RLE, ZIPS or ZIP compression."); }
            int height = header.DataWindow.Height, width = header.DataWindow.Width;
            int chunkCount = (height - 1) / header.LinesPerChunk + 1;
            if (header.ChunkCount != chunkCount) { throw new InvalidDataException("OpenEXR chunk count disagrees with its data window."); }
            int[] components = SelectChannels(header, layer);
            int rowBytes = 0;
            foreach (ExrChannel channel in header.Channels)
            {
                if (channel.SamplingX != 1 || channel.SamplingY != 1)
                { throw new NotSupportedException("OpenEXR subsampled channels require a reconstruction operation."); }
                rowBytes = checked(rowBytes + width * channel.Bytes);
            }
            int scratchBytes = header.Compression == 0 ? 0 : checked(rowBytes * Math.Min(header.LinesPerChunk, height) * 2);
            VideoFormat format = ExrVideoFormat.Create(header);
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer output = allocator.Allocate(checked((int)layout[0].EndOffset));
            using WritableMediaBuffer? scratch = scratchBytes == 0 ? null : allocator.Allocate(scratchBytes);
            using ExrCompression compression = new();
            Span<float> destination = MemoryMarshal.Cast<byte, float>(output.Span);
            if (!components.Contains(3))
            {
                for (int y = 0; y < height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    Span<float> row = destination.Slice(y * width * 4, width * 4);
                    for (int x = 0; x < width; x++) { row[x * 4 + 3] = 1; }
                }
            }
            for (int chunk = 0; chunk < chunkCount; chunk++)
            {
                token.ThrowIfCancellationRequested();
                ExrCursor cursor = document.Chunk(header, chunk);
                if (document.Multipart && cursor.Int() != partIndex) { throw new InvalidDataException("OpenEXR chunk belongs to another part."); }
                int firstY = cursor.Int();
                if ((long)firstY - header.DataWindow.Top != (long)chunk * header.LinesPerChunk)
                { throw new InvalidDataException("OpenEXR chunk scan line disagrees with its index."); }
                int rows = Math.Min(header.LinesPerChunk, height - chunk * header.LinesPerChunk);
                ReadOnlySpan<byte> encoded = cursor.Take(cursor.Int()).Span;
                ReadOnlySpan<byte> decoded = compression.Decode(encoded, scratch is null ? Span<byte>.Empty : scratch.Span,
                    checked(rows * rowBytes), header.Compression, token);
                WriteRows(decoded, destination.Slice(chunk * header.LinesPerChunk * width * 4), rows, width, header.Channels, components, token);
            }
            token.ThrowIfCancellationRequested();
            using MediaBuffer pixels = output.Publish();
            return new(format, pixels, layout);
        }

        private static int[] SelectChannels(ExrHeader header, string layer)
        {
            string prefix = layer.Length == 0 ? string.Empty : layer + ".";
            bool rgb = header.Channels.Any(channel => channel.Name == prefix + "R")
                && header.Channels.Any(channel => channel.Name == prefix + "G") && header.Channels.Any(channel => channel.Name == prefix + "B");
            bool luma = header.Channels.Any(channel => channel.Name == prefix + "Y");
            if (!rgb && !luma) { throw new NotSupportedException("OpenEXR selected layer requires R/G/B or Y channels."); }
            int[] selected = new int[header.Channels.Count];
            for (int index = 0; index < selected.Length; index++)
            {
                ExrChannel channel = header.Channels[index];
                selected[index] = channel.Name.StartsWith(prefix, StringComparison.Ordinal) ? channel.Name[prefix.Length..] switch
                {
                    "R" when rgb => 0,
                    "G" when rgb => 1,
                    "B" when rgb => 2,
                    "A" => 3,
                    "Y" when !rgb => 4,
                    _ => -1
                } : -1;
                if (selected[index] >= 0 && channel.Type == 0)
                { throw new NotSupportedException("OpenEXR color UINT samples cannot be preserved exactly in float32 output."); }
            }
            return selected;
        }

        private static void WriteRows(ReadOnlySpan<byte> source, Span<float> output, int rows, int width,
            IReadOnlyList<ExrChannel> channels, int[] components, CancellationToken token)
        {
            int offset = 0;
            for (int row = 0; row < rows; row++)
            {
                token.ThrowIfCancellationRequested();
                Span<float> target = output.Slice(row * width * 4, width * 4);
                for (int index = 0; index < channels.Count; index++)
                {
                    ExrChannel channel = channels[index]; int component = components[index];
                    int length = checked(width * channel.Bytes);
                    if (component >= 0)
                    {
                        ReadOnlySpan<byte> values = source.Slice(offset, length);
                        for (int x = 0; x < width; x++)
                        {
                            if ((x & 4095) == 0) { token.ThrowIfCancellationRequested(); }
                            float value = channel.Type switch
                            {
                                1 => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(values[(x * 2)..])),
                                2 => BinaryPrimitives.ReadSingleLittleEndian(values[(x * 4)..]),
                                _ => throw new InvalidDataException("Invalid selected OpenEXR channel representation.")
                            };
                            if (component == 4) { target[x * 4] = value; target[x * 4 + 1] = value; target[x * 4 + 2] = value; }
                            else { target[x * 4 + component] = value; }
                        }
                    }
                    offset += length;
                }
            }
            if (offset != source.Length) { throw new InvalidDataException("OpenEXR samples do not fill their chunk."); }
        }
    }
}
