// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder<T> where T : unmanaged, IBinaryInteger<T>
    {
        internal const int LeftContexts = 32;
        private readonly Av1SequenceHeader sequence;
        private readonly Av1FrameHeader header;
        private readonly Av1TileBounds bounds;
        private readonly Span<T> output;
        private readonly Span<ushort> probabilities;
        private readonly Span<byte> decoded;
        private readonly Span<Av1BlockContext<T>> contexts;
        private readonly Span<Av1BlockContext<T>> above;
        private readonly Span<Av1BlockContext<T>> left;
        private readonly CancellationToken cancellationToken;
        private readonly Av1ReferencePictures? references;
        private readonly Span<Av1MotionBlock> motionBlocks;
        private readonly Span<Av1FilterBlock> filterBlocks;
        private readonly Span<sbyte> cdefIndices;
        private readonly Span<Av1RestorationUnit> restorationUnits;
        private readonly ReadOnlySpan<Av1MotionFieldSample> motionField;
        private Av1RestorationReferences restorationReferences;
        private Av1SymbolReader symbols;
        private Av1Quantization quantization;
        private readonly Span<byte> segmentMap;
        private readonly ReadOnlySpan<byte> previousSegments;
        private readonly bool segmentsBeforeSkip;
        private readonly int lastActiveSegment;
        private int segmentId;
        private int currentQuantizer;
        private bool lossless;
        private Av1BlockFilterDeltas filterDeltas;
        private bool readDeltas;

        private int SampleBitDepth => typeof(T) == typeof(byte) ? 8 : sequence.BitDepth;

        internal Av1TileDecoder(ReadOnlySpan<byte> encoded, Av1SequenceHeader sequence, Av1FrameHeader header,
            Span<T> output, Span<ushort> probabilities, Span<byte> decoded, Span<Av1BlockContext<T>> contexts, CancellationToken cancellationToken, ReadOnlySpan<ushort> initialProbabilities = default, Av1ReferencePictures? references = null, Span<Av1MotionBlock> motionBlocks = default, Span<Av1FilterBlock> filterBlocks = default, Span<sbyte> cdefIndices = default, Span<Av1RestorationUnit> restorationUnits = default, ReadOnlySpan<Av1MotionFieldSample> motionField = default, Span<byte> segmentMap = default, ReadOnlySpan<byte> previousSegments = default, int tileIndex = 0)
        {
            if (sequence.BitDepth is not (8 or 10 or 12) || typeof(T) != typeof(byte) && typeof(T) != typeof(ushort) || typeof(T) == typeof(byte) && sequence.BitDepth != 8)
            {
                throw new ArgumentException("The AV1 sample storage cannot represent the sequence depth.", nameof(sequence));
            }
            bounds = header.Tiles.Bounds(tileIndex, sequence);
            if ((header.LoopFilter.Enabled || header.Cdef.Enabled) && filterBlocks.Length != sequence.MiColumns * sequence.MiRows)
            {
                throw new ArgumentException("The AV1 loop-filter metadata has an invalid extent.", nameof(filterBlocks));
            }
            if (cdefIndices.Length != header.Cdef.IndexCount(sequence))
            {
                throw new ArgumentException("The AV1 directional-filter index metadata has an invalid extent.", nameof(cdefIndices));
            }
            if (restorationUnits.Length != header.Restoration.UnitCount(header.PixelGeometry))
            {
                throw new ArgumentException("The AV1 restoration metadata has an invalid extent.", nameof(restorationUnits));
            }
            if (motionField.Length != (header.Inter.ReferenceMotion ? sequence.MiColumns * sequence.MiRows / 4 : 0))
            {
                throw new ArgumentException("The AV1 temporal motion-field metadata has an invalid extent.", nameof(motionField));
            }
            int segmentCount = header.Segmentation.Enabled ? sequence.MiColumns * sequence.MiRows : 0;
            bool mapLengthValid = segmentMap.Length == segmentCount || header.Segmentation.Enabled && !header.Segmentation.UpdateMap && segmentMap.IsEmpty;
            if (!mapLengthValid || !previousSegments.IsEmpty && previousSegments.Length != segmentCount)
            {
                throw new ArgumentException("The AV1 segmentation buffers have invalid extents.", nameof(segmentMap));
            }
            this.segmentMap = segmentMap;
            this.previousSegments = previousSegments;
            segmentsBeforeSkip = header.Segmentation.PreSkip;
            lastActiveSegment = header.Segmentation.LastActiveId;
            segmentId = 0;
            currentQuantizer = header.Quantization.BaseIndex;
            lossless = header.Quantization.Lossless;
            this.motionField = motionField;
            this.restorationUnits = restorationUnits;
            restorationReferences = default;
            for (int plane = 0; plane < 3; plane++) { restorationReferences[plane] = Av1RestorationUnit.Initial; }
            this.cdefIndices = cdefIndices;
            if (!cdefIndices.IsEmpty)
            {
                int columns = (sequence.MiColumns + 15) >> 4;
                int start = bounds.ColumnStart >> 4;
                int length = ((bounds.ColumnEnd + 15) >> 4) - start;
                for (int row = bounds.RowStart >> 4; row < (bounds.RowEnd + 15) >> 4; row++)
                {
                    cdefIndices.Slice(row * columns + start, length).Fill(-1);
                }
            }
            this.references = references;
            this.motionBlocks = motionBlocks;
            this.filterBlocks = filterBlocks;
            this.sequence = sequence;
            this.header = header;
            quantization = header.Quantization;
            filterDeltas = default;
            readDeltas = false;
            this.output = output;
            this.probabilities = probabilities;
            this.decoded = decoded;
            this.contexts = contexts;
            above = contexts[..sequence.MiColumns];
            left = contexts.Slice(sequence.MiColumns, LeftContexts);
            this.cancellationToken = cancellationToken;
            symbols = new(encoded, !header.DisableCdfUpdate);
            Av1TileProbabilities.Initialize(probabilities, header.Quantization.BaseIndex, initialProbabilities);
            int startColumn = bounds.ColumnStart;
            int tileColumns = bounds.ColumnEnd - startColumn;
            above.Slice(startColumn, tileColumns).Clear();
            if (!sequence.Monochrome)
            {
                CoefficientAbove(1).Slice(startColumn >> 1, tileColumns >> 1).Clear();
                CoefficientAbove(2).Slice(startColumn >> 1, tileColumns >> 1).Clear();
            }
        }

        internal void Decode()
        {
            int side = sequence.Superblock128 ? 32 : 16;
            for (int row = bounds.RowStart; row < bounds.RowEnd; row += side)
            {
                cancellationToken.ThrowIfCancellationRequested();
                left.Clear();
                if (!sequence.Monochrome)
                {
                    CoefficientLeft(1).Clear();
                    CoefficientLeft(2).Clear();
                }
                for (int column = bounds.ColumnStart; column < bounds.ColumnEnd; column += side)
                {
                    readDeltas = header.Deltas.QuantizerPresent;
                    ReadRestoration(row, column, side);
                    Partition(row, column, side);
                }
            }
            symbols.Finish();
            if (header.Segmentation.Enabled && !header.Segmentation.UpdateMap && !segmentMap.IsEmpty)
            {
                for (int row = bounds.RowStart; row < bounds.RowEnd; row++)
                {
                    int offset = row * sequence.MiColumns + bounds.ColumnStart;
                    Span<byte> region = segmentMap.Slice(offset, bounds.ColumnEnd - bounds.ColumnStart);
                    if (previousSegments.IsEmpty) { region.Clear(); }
                    else { previousSegments.Slice(offset, region.Length).CopyTo(region); }
                }
            }
        }

        private Span<Av1BlockContext<T>> CoefficientAbove(int plane)
        {
            int luma = sequence.MiColumns + LeftContexts;
            int chroma = luma / 2;
            return plane switch
            {
                0 => above,
                1 => contexts.Slice(luma, sequence.MiColumns / 2),
                2 => contexts.Slice(luma + chroma, sequence.MiColumns / 2),
                _ => throw new ArgumentOutOfRangeException(nameof(plane))
            };
        }

        private Span<Av1BlockContext<T>> CoefficientLeft(int plane)
        {
            int luma = sequence.MiColumns + LeftContexts;
            int chroma = luma / 2;
            return plane switch
            {
                0 => left,
                1 => contexts.Slice(luma + sequence.MiColumns / 2, LeftContexts / 2),
                2 => contexts.Slice(luma + chroma + sequence.MiColumns / 2, LeftContexts / 2),
                _ => throw new ArgumentOutOfRangeException(nameof(plane))
            };
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
                int context = (row > bounds.RowStart && above[column].WidthLog < log ? 1 : 0) +
                    (column > bounds.ColumnStart && left[row & (LeftContexts - 1)].HeightLog < log ? 2 : 0);
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
