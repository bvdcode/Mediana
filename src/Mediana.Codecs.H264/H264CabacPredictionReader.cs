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

        public H264CabacPredictionReader(H264CabacReader reader, H264Picture target, MediaBufferAllocator allocator)
        {
            this.reader = reader;
            this.target = target;
            blocks = checked(target.Width * target.Height / 16);
            storage = allocator.Allocate(checked(blocks * sizeof(short) * 2));
        }

        private Span<short> MotionX => MemoryMarshal.Cast<byte, short>(storage.Memory.Span[..(blocks * sizeof(short))]);
        private Span<short> MotionY => MemoryMarshal.Cast<byte, short>(storage.Memory.Span[(blocks * sizeof(short))..]);

        public int ReadSubType(ref H264BitReader bits) => H264CabacMacroblockSyntax.ReadSubType(ref bits, reader);

        public int ReadReference(ref H264BitReader bits, int x, int y, int maximum)
        {
            int context = ReferenceCondition(x - 1, y) + 2 * ReferenceCondition(x, y - 1);
            return H264CabacMacroblockSyntax.ReadReference(ref bits, reader, context, maximum);
        }

        public (int X, int Y) ReadMotionDifference(ref H264BitReader bits, int x, int y, int width, int height)
        {
            int dx = H264CabacMacroblockSyntax.ReadMotionDifference(ref bits, reader, false,
                DifferenceAt(x - 1, y, MotionX) + DifferenceAt(x, y - 1, MotionX));
            int dy = H264CabacMacroblockSyntax.ReadMotionDifference(ref bits, reader, true,
                DifferenceAt(x - 1, y, MotionY) + DifferenceAt(x, y - 1, MotionY));
            for (int row = y; row < y + height; row += 4)
            {
                for (int column = x; column < x + width; column += 4)
                {
                    int index = target.BlockIndex(0, column, row);
                    MotionX[index] = (short)dx;
                    MotionY[index] = (short)dy;
                }
            }
            return (dx, dy);
        }

        private int DifferenceAt(int x, int y, Span<short> differences) =>
            x < 0 || y < 0 || x >= target.Width || y >= target.Height ? 0 : Math.Abs((int)differences[target.BlockIndex(0, x, y)]);

        private int ReferenceCondition(int x, int y)
        {
            if (x < 0 || y < 0 || x >= target.Width || y >= target.Height)
            {
                return 0;
            }
            byte index = target.ReferenceIndices(H264ReferenceList.L0)[target.BlockIndex(0, x, y)];
            return index != byte.MaxValue && index > 0 ? 1 : 0;
        }

        public void Dispose() => storage.Dispose();
    }
}
