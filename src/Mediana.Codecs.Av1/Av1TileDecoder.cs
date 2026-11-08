// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        internal const int LeftContexts = 32;
        private readonly Av1SequenceHeader sequence;
        private readonly Av1FrameHeader header;
        private readonly Span<byte> output;
        private readonly Span<ushort> probabilities;
        private readonly Span<Av1BlockContext> above;
        private readonly Span<Av1BlockContext> left;
        private readonly CancellationToken cancellationToken;
        private Av1SymbolReader symbols;

        internal Av1TileDecoder(ReadOnlySpan<byte> encoded, Av1SequenceHeader sequence, Av1FrameHeader header,
            Span<byte> output, Span<ushort> probabilities, Span<Av1BlockContext> contexts, CancellationToken cancellationToken)
        {
            this.sequence = sequence;
            this.header = header;
            this.output = output;
            this.probabilities = probabilities;
            above = contexts[..sequence.MiColumns];
            left = contexts[sequence.MiColumns..];
            this.cancellationToken = cancellationToken;
            symbols = new(encoded, !header.DisableCdfUpdate);
            Av1DefaultCdfs.Values.CopyTo(probabilities);
        }

        internal void Decode()
        {
            int side = sequence.Superblock128 ? 32 : 16;
            for (int row = 0; row < sequence.MiRows; row += side)
            {
                cancellationToken.ThrowIfCancellationRequested();
                left.Clear();
                for (int column = 0; column < sequence.MiColumns; column += side)
                {
                    Partition(row, column, side);
                }
            }
            symbols.Finish();
        }

        private void Partition(int row, int column, int side)
        {
            if (row >= sequence.MiRows || column >= sequence.MiColumns)
            {
                return;
            }
            int half = side >> 1;
            int quarter = side >> 2;
            bool hasRows = row + half < sequence.MiRows;
            bool hasColumns = column + half < sequence.MiColumns;
            int partition;
            if (side == 1)
            {
                partition = 0;
            }
            else
            {
                int log = BitOperations.Log2((uint)side);
                int context = (row > 0 && above[column].WidthLog < log ? 1 : 0) +
                    (column > 0 && left[row & (LeftContexts - 1)].HeightLog < log ? 2 : 0);
                Span<ushort> cdf = PartitionCdf(log, context);
                if (hasRows && hasColumns)
                {
                    partition = symbols.ReadSymbol(cdf);
                }
                else if (hasColumns || hasRows)
                {
                    int sum;
                    if (hasColumns)
                    {
                        sum = Mass(cdf, 2) + Mass(cdf, 3) + Mass(cdf, 4) + Mass(cdf, 6) + Mass(cdf, 7);
                        if (side != 32)
                        {
                            sum += Mass(cdf, 9);
                        }
                    }
                    else
                    {
                        sum = Mass(cdf, 1) + Mass(cdf, 3) + Mass(cdf, 4) + Mass(cdf, 5) + Mass(cdf, 6);
                        if (side != 32)
                        {
                            sum += Mass(cdf, 8);
                        }
                    }
                    Span<ushort> boundary = stackalloc ushort[3];
                    boundary[0] = checked((ushort)(Av1Cdf.Top - sum));
                    boundary[1] = Av1Cdf.Top;
                    boundary[2] = 0;
                    int split = symbols.ReadSymbol(boundary);
                    partition = split != 0 ? 3 : hasColumns ? 1 : 2;
                }
                else
                {
                    partition = 3;
                }
            }
            switch (partition)
            {
                case 0:
                    Block(row, column, side, side);
                    break;
                case 1:
                    Block(row, column, side, half);
                    if (hasRows) { Block(row + half, column, side, half); }
                    break;
                case 2:
                    Block(row, column, half, side);
                    if (hasColumns) { Block(row, column + half, half, side); }
                    break;
                case 3:
                    Partition(row, column, half);
                    Partition(row, column + half, half);
                    Partition(row + half, column, half);
                    Partition(row + half, column + half, half);
                    break;
                case 4:
                    Block(row, column, half, half);
                    Block(row, column + half, half, half);
                    Block(row + half, column, side, half);
                    break;
                case 5:
                    Block(row, column, side, half);
                    Block(row + half, column, half, half);
                    Block(row + half, column + half, half, half);
                    break;
                case 6:
                    Block(row, column, half, half);
                    Block(row + half, column, half, half);
                    Block(row, column + half, half, side);
                    break;
                case 7:
                    Block(row, column, half, side);
                    Block(row, column + half, half, half);
                    Block(row + half, column + half, half, half);
                    break;
                case 8:
                    for (int index = 0; index < 4 && row + quarter * index < sequence.MiRows; index++)
                    {
                        Block(row + quarter * index, column, side, quarter);
                    }
                    break;
                case 9:
                    for (int index = 0; index < 4 && column + quarter * index < sequence.MiColumns; index++)
                    {
                        Block(row, column + quarter * index, quarter, side);
                    }
                    break;
                default:
                    throw new InvalidDataException("The AV1 partition type is invalid.");
            }
        }

        private Span<ushort> PartitionCdf(int log, int context)
        {
            (int offset, int length) = log switch
            {
                1 => (Av1DefaultCdfs.Partition8Offset, 5),
                2 => (Av1DefaultCdfs.Partition16Offset, 11),
                3 => (Av1DefaultCdfs.Partition32Offset, 11),
                4 => (Av1DefaultCdfs.Partition64Offset, 11),
                5 => (Av1DefaultCdfs.Partition128Offset, 9),
                _ => throw new InvalidDataException("The AV1 partition size is invalid.")
            };
            return probabilities.Slice(offset + context * length, length);
        }

        private static int Mass(ReadOnlySpan<ushort> cdf, int symbol) => cdf[symbol] - cdf[symbol - 1];
    }
}
