// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Vp9
{
    internal ref partial struct Vp9ImageDecoder<T> where T : unmanaged, IBinaryInteger<T>
    {
        private readonly Vp9FrameHeader header;
        private readonly Vp9HeaderState state;
        private readonly Vp9CompressedHeader compressed;
        private readonly Vp9ProbabilityContext probabilities;
        private readonly Vp9CoefficientCounts? counts;
        private readonly Span<T> pixels;
        private readonly Vp9ReferencePictures? references;
        private readonly ReadOnlySpan<Vp9BlockInfo> previous;
        private readonly bool usePreviousMotion;
        private readonly Vp9SymbolCounts symbols;
        private readonly Span<T> predictionWorkspace;
        private readonly Span<int> coefficientWorkspace;
        private readonly Span<byte> aboveSegment, leftSegment;
        private readonly Span<Vp9BlockInfo> blocks;
        private readonly Span<byte> abovePartition, leftPartition, aboveNonzero, leftNonzero;
        private readonly int stride, height, yLength, uvLength, subX, subY, miStride;
        private Vp9Tile tile;
        private readonly CancellationToken cancellationToken;

        internal Vp9ImageDecoder(Vp9FrameHeader header, Vp9HeaderState state, Vp9CompressedHeader compressed,
            Vp9ProbabilityContext probabilities, Span<byte> output, MediaBufferWorkspace workspace,
            Vp9CoefficientCounts? counts, CancellationToken cancellationToken, Vp9SymbolCounts? symbols = null,
            Vp9ReferencePictures? references = null, ReadOnlySpan<Vp9BlockInfo> previous = default, Span<Vp9BlockInfo> blockStorage = default, bool usePreviousMotion = false)
        {
            this.header = header; this.state = state; this.compressed = compressed; this.probabilities = probabilities;
            this.counts = counts; this.cancellationToken = cancellationToken; this.references = references; this.previous = previous;
            this.symbols = symbols ?? new(); this.usePreviousMotion = usePreviousMotion;
            stride = header.SuperblockColumns * 64; height = header.SuperblockRows * 64; miStride = stride / 8;
            subX = header.Geometry.SubsamplingX ? 1 : 0; subY = header.Geometry.SubsamplingY ? 1 : 0;
            yLength = checked(stride * height); uvLength = yLength >> (subX + subY);
            coefficientWorkspace = MemoryMarshal.Cast<byte, int>(workspace.Allocate(1024 * sizeof(int)).Span);
            pixels = MemoryMarshal.Cast<byte, T>(output)[..checked(yLength + 2 * uvLength)];
            blocks = blockStorage.IsEmpty ? MemoryMarshal.Cast<byte, Vp9BlockInfo>(workspace.Allocate(checked(miStride * (height / 8) * Unsafe.SizeOf<Vp9BlockInfo>())).Span) : blockStorage;
            blocks.Clear();
            abovePartition = workspace.Allocate(miStride).Span; abovePartition.Clear();
            leftPartition = workspace.Allocate(8).Span;
            aboveNonzero = workspace.Allocate(checked(miStride * 2 * 3)).Span; aboveNonzero.Clear();
            leftNonzero = workspace.Allocate(16 * 3).Span;
            aboveSegment = workspace.Allocate(miStride).Span; aboveSegment.Clear(); leftSegment = workspace.Allocate(8).Span;
            predictionWorkspace = header.IsIntra ? default : MemoryMarshal.Cast<byte, T>(workspace.Allocate(checked(Vp9InterPrediction.WorkspaceLength(64, 64, 32) * Unsafe.SizeOf<T>())).Span);
        }

        internal void Decode(ReadOnlyMemory<byte> encoded)
        {
            Vp9TileReader tiles = new(encoded, header);
            while (tiles.MoveNext())
            {
                tile = tiles.Current; Vp9BooleanReader bits = new(tile.Encoded.Span);
                for (int row = tile.RowStart; row < tile.RowEnd; row += 8)
                {
                    cancellationToken.ThrowIfCancellationRequested(); leftPartition.Clear(); leftNonzero.Clear(); leftSegment.Clear();
                    for (int column = tile.ColumnStart; column < tile.ColumnEnd; column += 8) { Partition(ref bits, row, column, 3); }
                }
                bits.Finish();
            }
        }

        private void Partition(ref Vp9BooleanReader bits, int row, int column, int logMi)
        {
            if (row >= header.MiRows || column >= header.MiColumns) { return; }
            int extent = 1 << logMi, half = extent >> 1, context = logMi * 4;
            int above = 0, left = 0;
            for (int i = 0; i < extent; i++) { above |= abovePartition[column + i]; left |= leftPartition[(row + i) & 7]; }
            int mask = 1 << (3 - logMi);
            context += (above & mask) != 0 ? 1 : 0; context += (left & mask) != 0 ? 2 : 0;
            ReadOnlySpan<byte> p = header.IsIntra ? Vp9Tables.KeyPartition.Slice(context * 3, 3) : probabilities.Partition.Slice(context * 3, 3);
            bool hasRows = row + half < header.MiRows, hasColumns = column + half < header.MiColumns;
            int partition;
            if (hasRows && hasColumns) { partition = bits.Tree(PartitionTree, p); }
            else if (hasColumns) { partition = bits.Read(p[1]) ? 3 : 1; }
            else if (hasRows) { partition = bits.Read(p[2]) ? 3 : 2; }
            else { partition = 3; }
            if (!header.IsIntra)
            {
                int offset = Vp9ProbabilityOffset.Partition + context * 3;
                symbols.Record(offset, partition != 0);
                if (partition != 0) { symbols.Record(offset + 1, partition != 1); }
                if (partition > 1) { symbols.Record(offset + 2, partition == 3); }
            }
            int square = (logMi + 1) * 3;
            int size = partition switch { 0 => square, 1 => square - 1, 2 => square - 2, 3 => square - 3, _ => throw new InvalidDataException("Invalid VP9 partition.") };
            if (size < 3 || partition == 0) { Block(ref bits, row, column, size); }
            else if (partition == 1)
            { Block(ref bits, row, column, size); if (hasRows) { Block(ref bits, row + half, column, size); } }
            else if (partition == 2)
            { Block(ref bits, row, column, size); if (hasColumns) { Block(ref bits, row, column + half, size); } }
            else
            {
                Partition(ref bits, row, column, logMi - 1); Partition(ref bits, row, column + half, logMi - 1);
                Partition(ref bits, row + half, column, logMi - 1); Partition(ref bits, row + half, column + half, logMi - 1);
            }
            if (logMi == 0 || partition != 3)
            {
                byte valueX = (byte)(15 >> (BitOperations.Log2((uint)Widths[size]) - 2));
                byte valueY = (byte)(15 >> (BitOperations.Log2((uint)Heights[size]) - 2));
                abovePartition.Slice(column, extent).Fill(valueX);
                for (int i = 0; i < extent; i++) { leftPartition[(row + i) & 7] = valueY; }
            }
        }

        private static ReadOnlySpan<byte> Widths => [4, 4, 8, 8, 8, 16, 16, 16, 32, 32, 32, 64, 64];
        private static ReadOnlySpan<byte> Heights => [4, 8, 4, 8, 16, 8, 16, 32, 16, 32, 64, 32, 64];
        private static ReadOnlySpan<sbyte> PartitionTree => [0, 2, -1, 4, -2, -3];
        private static ReadOnlySpan<sbyte> IntraTree => [0, 2, -9, 4, -1, 6, 8, 12, -2, 10, -4, -5, -3, 14, -8, 16, -6, -7];
        private static ReadOnlySpan<sbyte> SegmentTree => [2, 4, 6, 8, 10, 12, 0, -1, -2, -3, -4, -5, -6, -7];
    }
}
