// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Ffv1
{
    internal class Ffv1SlicedPicture(Ffv1SliceConfiguration configuration, MediaBufferAllocator allocator)
    {
        private readonly List<Ffv1SliceState> states = [];
        private bool opened;

        public VideoFrame Decode(EncodedStreamInfo stream, Ffv1StreamConfiguration geometry, MediaPacket packet, CancellationToken token)
        {
            ReadOnlySpan<byte> data = packet.Data.Span;
            int gridSize = configuration.Rows * configuration.Columns;
            Span<int> sliceStarts = stackalloc int[gridSize];
            int count = GetSliceStarts(data, sliceStarts);
            Span<bool> covered = stackalloc bool[gridSize];
            covered.Clear();
            int stride = geometry.Width;
            VideoFormat baseFormat = Ffv1VideoFormat.Create(stream, geometry, configuration.Format);
            PlaneLayout[] planes = baseFormat.CreateTightLayout();
            int bytes = checked(planes[^1].Offset + planes[^1].Height * planes[^1].Stride);
            using WritableMediaBuffer pixels = allocator.Allocate(bytes);
            Ffv1SliceHeader first = default;
            bool key = false;
            for (int index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                int start = sliceStarts[gridSize - count + index];
                int end = index + 1 == count ? data.Length : sliceStarts[gridSize - count + index + 1];
                ReadOnlySpan<byte> slice = data[start..end];
                Ffv1RangeReader range = new(slice);
                if (index == 0)
                {
                    key = Ffv1FrameModel.ReadKeyFrame(ref range);
                    if (!key && (!opened || configuration.IntraOnly || states.Count != count))
                    {
                        throw new InvalidDataException("The FFV1 sliced frame requires an opening keyframe.");
                    }
                }
                if (configuration.Transitions is byte[] transitions) { range.SetTransitions(transitions); }
                Ffv1SliceHeader header = Ffv1SliceHeader.Read(ref range, configuration);
                if (index == 0) { first = header; }
                if (first.Aspect != header.Aspect || first.Scan != header.Scan)
                {
                    throw new InvalidDataException("FFV1 slices disagree on picture aspect ratio or structure.");
                }
                MarkCoverage(header, covered);
                if (states.Count == index) { states.Add(new()); }
                Ffv1SliceState state = states[index];
                state.Prepare(header, configuration, allocator, key);
                DecodeSlice(slice, ref range, state, header, stride, geometry.Height, planes, pixels.Span, token);
            }
            if (covered.Contains(false)) { throw new InvalidDataException("FFV1 slices do not cover the whole picture."); }
            for (int index = states.Count - 1; index >= count; index--)
            {
                states[index].Dispose();
                states.RemoveAt(index);
            }
            opened = true;
            VideoFormat format = Ffv1VideoFormat.Create(stream, geometry, configuration.Format, first.Aspect, first.Scan);
            using MediaBuffer buffer = pixels.Publish();
            return new(format, buffer, planes, packet.PresentationTime ?? packet.DecodeTime, packet.Duration);
        }

        private int GetSliceStarts(ReadOnlySpan<byte> data, Span<int> starts)
        {
            int footer = configuration.HasCrc ? 8 : 3;
            int end = data.Length;
            int count = 0;
            while (end != 0)
            {
                if (end < footer + 2 || count == starts.Length)
                {
                    throw new InvalidDataException("Truncated FFV1 slice or excessive slice count.");
                }
                int trailer = end - footer;
                int size = (data[trailer] << 16) | (data[trailer + 1] << 8) | data[trailer + 2];
                int start = trailer - size;
                if (size < 2 || start < 0 || (configuration.HasCrc && (data[trailer + 3] != 0 || !Ffv1Crc.IsValid(data[start..end]))))
                {
                    throw new InvalidDataException("Invalid FFV1 slice length, error status or checksum.");
                }
                starts[^(++count)] = start;
                end = start;
            }
            if (count == 0) { throw new InvalidDataException("The FFV1 frame has no slices."); }
            return count;
        }

        private void MarkCoverage(Ffv1SliceHeader header, Span<bool> coverage)
        {
            for (int row = header.Y; row < header.Y + header.Height; row++)
            {
                Span<bool> cells = coverage.Slice(row * configuration.Columns + header.X, header.Width);
                if (cells.Contains(true)) { throw new InvalidDataException("FFV1 slices overlap."); }
                cells.Fill(true);
            }
        }

        private void DecodeSlice(ReadOnlySpan<byte> slice, scoped ref Ffv1RangeReader range,
            Ffv1SliceState state, Ffv1SliceHeader header, int width, int height, PlaneLayout[] planes,
            scoped Span<byte> pixels, CancellationToken token)
        {
            int x = Scale(header.X, width, configuration.Columns);
            int y = Scale(header.Y, height, configuration.Rows);
            int right = Scale(header.X + header.Width, width, configuration.Columns);
            int bottom = Scale(header.Y + header.Height, height, configuration.Rows);
            Ffv1RiceReader rice = new([]);
            if (configuration.Rice)
            {
                if (configuration.MinorVersion > 1)
                {
                    byte endState = 129;
                    range.ReadBit(ref endState);
                }
                int end = slice.Length - (configuration.HasCrc ? 8 : 3);
                if (range.RiceOffset >= end) { throw new InvalidDataException("The FFV1 slice header exceeds its payload."); }
                rice = new(slice[range.RiceOffset..end]);
            }
            PlanarVideoPixelFormat pixelFormat = (PlanarVideoPixelFormat)configuration.Format;
            for (int plane = 0; plane < planes.Length; plane++)
            {
                int sx = plane == 0 ? 1 : pixelFormat.HorizontalSubsampling;
                int sy = plane == 0 ? 1 : pixelFormat.VerticalSubsampling;
                PlaneLayout target = planes[plane];
                int rows = (bottom - y + sy - 1) / sy;
                int columns = (right - x + sx - 1) / sx;
                PlaneLayout region = new(checked(target.Offset + y / sy * target.Stride + x / sx), target.Stride, columns, rows);
                if (columns > target.RowBytes - x / sx || rows > target.Height - y / sy)
                {
                    throw new InvalidDataException("The FFV1 slice exceeds the decoded plane.");
                }
                Ffv1Quantizer quantizer = configuration.Quantizers[plane == 0 ? header.LumaTable : header.ChromaTable];
                Span<byte> contexts = plane == 0 ? state.Luma : state.Chroma;
                Ffv1PlaneDecoder.Decode(ref range, ref rice, pixels, region, configuration.Rice, quantizer, contexts, token);
            }
            if (!configuration.Rice)
            {
                byte endState = 129;
                range.ReadBit(ref endState);
                if (range.BytesRead != slice.Length - 2 - (configuration.HasCrc ? 5 : 0))
                {
                    throw new InvalidDataException("The FFV1 range payload does not end at its declared slice boundary.");
                }
            }
        }

        private static int Scale(int value, int size, int divisions) => (int)((long)value * size / divisions);

        public void Reset()
        {
            foreach (Ffv1SliceState state in states) { state.Dispose(); }
            states.Clear();
            opened = false;
        }
    }
}
