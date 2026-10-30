// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using System.Runtime.InteropServices;

namespace Mediana.Codecs.H264
{
    internal class H264CabacPredictionReader : IH264PredictionReader, IDisposable
    {
        private readonly H264CabacReader reader;
        private readonly H264Picture target;
        private readonly WritableMediaBuffer storage;
        private readonly int blocks;
        private readonly int listBytes;

        public H264CabacPredictionReader(H264CabacReader reader, H264Picture target, MediaBufferAllocator allocator)
        {
            this.reader = reader;
            this.target = target;
            blocks = checked(target.Width * target.Height / 16);
            listBytes = checked(blocks * (sizeof(short) * 2 + (target.HasBidirectionalMotion ? 1 : 0)));
            storage = allocator.Allocate(checked(listBytes * (target.HasBidirectionalMotion ? 2 : 1)));
        }

        private Span<short> MotionX(H264ReferenceList list) => MemoryMarshal.Cast<byte, short>(storage.Memory.Span.Slice(ListOffset(list), blocks * sizeof(short)));
        private Span<short> MotionY(H264ReferenceList list) => MemoryMarshal.Cast<byte, short>(storage.Memory.Span.Slice(ListOffset(list) + blocks * sizeof(short), blocks * sizeof(short)));
        private Span<byte> ReferenceConditions(H264ReferenceList list) => storage.Memory.Span.Slice(ListOffset(list) + blocks * sizeof(short) * 2, blocks);

        public int ReadSubType(ref H264BitReader bits, H264SliceKind kind) => kind switch
        {
            H264SliceKind.P => H264CabacMacroblockSyntax.ReadSubType(ref bits, reader),
            H264SliceKind.B => H264CabacMacroblockSyntax.ReadBidirectionalSubType(ref bits, reader),
            H264SliceKind.I or H264SliceKind.Sp or H264SliceKind.Si => throw new InvalidDataException("The slice has no supported inter sub-macroblock syntax."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public int ReadReference(ref H264BitReader bits, H264ReferenceList list, int x, int y, int width, int height, int maximum)
        {
            int context = ReferenceCondition(list, x - 1, y) + 2 * ReferenceCondition(list, x, y - 1);
            int reference = H264CabacMacroblockSyntax.ReadReference(ref bits, reader, context, maximum);
            if (target.HasBidirectionalMotion && reference > 0)
            {
                Span<byte> conditions = ReferenceConditions(list);
                for (int row = y; row < y + height; row += 4)
                {
                    for (int column = x; column < x + width; column += 4)
                    {
                        conditions[target.BlockIndex(0, column, row)] = 1;
                    }
                }
            }
            return reference;
        }

        public (int X, int Y) ReadMotionDifference(ref H264BitReader bits, H264ReferenceList list, int x, int y, int width, int height)
        {
            Span<short> motionX = MotionX(list);
            Span<short> motionY = MotionY(list);
            int dx = H264CabacMacroblockSyntax.ReadMotionDifference(ref bits, reader, false,
                DifferenceAt(x - 1, y, motionX) + DifferenceAt(x, y - 1, motionX));
            int dy = H264CabacMacroblockSyntax.ReadMotionDifference(ref bits, reader, true,
                DifferenceAt(x - 1, y, motionY) + DifferenceAt(x, y - 1, motionY));
            for (int row = y; row < y + height; row += 4)
            {
                for (int column = x; column < x + width; column += 4)
                {
                    int index = target.BlockIndex(0, column, row);
                    motionX[index] = (short)dx;
                    motionY[index] = (short)dy;
                }
            }
            return (dx, dy);
        }

        private int DifferenceAt(int x, int y, Span<short> differences) =>
            !target.IsInCurrentSlice(x, y) ? 0 : Math.Abs((int)differences[target.BlockIndex(0, x, y)]);

        private int ReferenceCondition(H264ReferenceList list, int x, int y)
        {
            if (!target.IsInCurrentSlice(x, y))
            {
                return 0;
            }
            if (target.HasBidirectionalMotion)
            {
                return ReferenceConditions(list)[target.BlockIndex(0, x, y)];
            }
            byte index = target.ReferenceIndices(list)[target.BlockIndex(0, x, y)];
            return index != byte.MaxValue && index > 0 ? 1 : 0;
        }

        private int ListOffset(H264ReferenceList list) => list switch
        {
            H264ReferenceList.L0 => 0,
            H264ReferenceList.L1 when target.HasBidirectionalMotion => listBytes,
            H264ReferenceList.L1 => throw new InvalidOperationException("The CABAC picture has no list 1 syntax state."),
            _ => throw new ArgumentOutOfRangeException(nameof(list))
        };

        public void Dispose() => storage.Dispose();
    }
}
