using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal class H264CabacMacroblockState : IDisposable
    {
        public const byte Inter = 26;
        public const byte Skipped = 27;
        private readonly WritableMediaBuffer storage;
        private readonly int count;
        private readonly int columns;

        public H264CabacMacroblockState(H264SequenceParameterSet sequence, MediaBufferAllocator allocator)
        {
            columns = sequence.WidthInMbs;
            count = checked(columns * sequence.HeightInMapUnits);
            storage = allocator.Allocate(checked(count * 4));
        }

        public Span<byte> Types => storage.Memory.Span[..count];
        public Span<byte> Patterns => storage.Memory.Span.Slice(count, count);
        public Span<byte> ChromaModes => storage.Memory.Span.Slice(count * 2, count);
        public Span<byte> DcFlags => storage.Memory.Span.Slice(count * 3, count);

        public int Left(int address) => address % columns == 0 ? -1 : address - 1;
        public int Top(int address) => address < columns ? -1 : address - columns;

        public int TypeCondition(int neighbour) => neighbour >= 0 && Types[neighbour] != 0 ? 1 : 0;
        public int SkipCondition(int neighbour) => neighbour >= 0 && Types[neighbour] != Skipped ? 1 : 0;
        public bool IsIntra(int address) => Types[address] <= 25;

        public int ChromaModeCondition(int neighbour) =>
            neighbour >= 0 && Types[neighbour] != 25 && ChromaModes[neighbour] != 0 ? 1 : 0;

        public int ChromaPatternCondition(int neighbour, int bin)
        {
            if (neighbour < 0)
            {
                return 0;
            }
            if (Types[neighbour] == 25)
            {
                return 1;
            }
            int chroma = Patterns[neighbour] >> 4;
            return bin == 0 ? (chroma != 0 ? 1 : 0) : (chroma == 2 ? 1 : 0);
        }

        public int LumaPatternCondition(int address, int block, bool left, int currentPattern)
        {
            int column = block % 2;
            int row = block / 2;
            int neighbour = address;
            if (left)
            {
                if (column == 0)
                {
                    neighbour = Left(address);
                    column = 1;
                }
                else
                {
                    column--;
                }
            }
            else if (row == 0)
            {
                neighbour = Top(address);
                row = 1;
            }
            else
            {
                row--;
            }
            if (neighbour < 0 || Types[neighbour] == 25)
            {
                return 0;
            }
            int pattern = neighbour == address ? currentPattern : Patterns[neighbour];
            return (pattern & (1 << (row * 2 + column))) == 0 ? 1 : 0;
        }

        public int DcCondition(int neighbour, int plane, bool currentIsIntra)
        {
            if (neighbour < 0)
            {
                return currentIsIntra ? 1 : 0;
            }
            return Types[neighbour] == 25 || (DcFlags[neighbour] & (1 << plane)) != 0 ? 1 : 0;
        }

        public void Dispose() => storage.Dispose();
    }
}
