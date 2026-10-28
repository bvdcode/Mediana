using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal class H264Picture : IDisposable
    {
        private readonly WritableMediaBuffer storage;
        private readonly int lumaBytes;
        private readonly int chromaBytes;
        private readonly int lumaBlocks;
        private readonly int chromaBlocks;

        public H264Picture(H264SequenceParameterSet sequence, MediaBufferAllocator allocator)
        {
            Sequence = sequence;
            Width = sequence.CodedWidth;
            Height = sequence.CodedHeight;
            lumaBytes = checked(Width * Height);
            chromaBytes = lumaBytes / 4;
            lumaBlocks = lumaBytes / 16;
            chromaBlocks = chromaBytes / 16;
            int macroblocks = lumaBytes / 256;
            storage = allocator.Allocate(checked(lumaBytes + chromaBytes * 2 + lumaBlocks * 2 + chromaBlocks * 2 + macroblocks));
            Modes.Fill(byte.MaxValue);
        }

        public H264SequenceParameterSet Sequence { get; }
        public int Width { get; }
        public int Height { get; }
        public Span<byte> Modes => storage.Memory.Span.Slice(lumaBytes + chromaBytes * 2 + lumaBlocks + chromaBlocks * 2, lumaBlocks);
        public Span<byte> Quantizers => storage.Memory.Span[(lumaBytes + chromaBytes * 2 + lumaBlocks * 2 + chromaBlocks * 2)..];

        public Span<byte> Plane(int index) => index switch
        {
            0 => storage.Memory.Span[..lumaBytes],
            1 => storage.Memory.Span.Slice(lumaBytes, chromaBytes),
            2 => storage.Memory.Span.Slice(lumaBytes + chromaBytes, chromaBytes),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        public Span<byte> Counts(int index) => index switch
        {
            0 => storage.Memory.Span.Slice(lumaBytes + chromaBytes * 2, lumaBlocks),
            1 => storage.Memory.Span.Slice(lumaBytes + chromaBytes * 2 + lumaBlocks, chromaBlocks),
            2 => storage.Memory.Span.Slice(lumaBytes + chromaBytes * 2 + lumaBlocks + chromaBlocks, chromaBlocks),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        public int BlockIndex(int plane, int x, int y) => (y / 4) * (Stride(plane) / 4) + x / 4;
        public int Stride(int plane) => plane == 0 ? Width : Width / 2;

        public int NeighbourCount(int plane, int x, int y)
        {
            Span<byte> counts = Counts(plane);
            int index = BlockIndex(plane, x, y);
            int left = x == 0 ? 0 : counts[index - 1];
            int top = y == 0 ? 0 : counts[index - Stride(plane) / 4];
            return x == 0 ? top : y == 0 ? left : (left + top + 1) >> 1;
        }

        public int PredictedMode(int x, int y)
        {
            if (x == 0 || y == 0)
            {
                return 2;
            }
            int index = BlockIndex(0, x, y);
            return Math.Min(Modes[index - 1] & 127, Modes[index - Width / 4] & 127);
        }

        public void SetLumaMode(int x, int y, int mode) => Modes[BlockIndex(0, x, y)] = (byte)(mode | 128);

        public void PredictLuma4x4(int x, int y, int mode, Span<byte> output)
        {
            Span<byte> top = stackalloc byte[8];
            Span<byte> left = stackalloc byte[4];
            Span<byte> pixels = Plane(0);
            bool hasTop = IsAvailable(x, y - 1);
            bool hasLeft = IsAvailable(x - 1, y);
            if (hasTop)
            {
                for (int index = 0; index < 8; index++)
                {
                    top[index] = IsAvailable(x + index, y - 1) ? pixels[(y - 1) * Width + x + index] : top[3];
                }
            }
            if (hasLeft)
            {
                for (int index = 0; index < 4; index++)
                {
                    left[index] = pixels[(y + index) * Width + x - 1];
                }
            }
            byte? corner = IsAvailable(x - 1, y - 1) ? pixels[(y - 1) * Width + x - 1] : null;
            H264IntraPrediction.Luma4x4(mode, hasTop ? top : [], hasLeft ? left : [], corner, output);
        }

        public void PredictMacroblock(int plane, int x, int y, int mode, Span<byte> output)
        {
            int size = plane == 0 ? 16 : 8;
            int stride = Stride(plane);
            Span<byte> pixels = Plane(plane);
            Span<byte> top = stackalloc byte[16];
            Span<byte> left = stackalloc byte[16];
            if (y > 0)
            {
                pixels.Slice((y - 1) * stride + x, size).CopyTo(top);
            }
            if (x > 0)
            {
                for (int index = 0; index < size; index++)
                {
                    left[index] = pixels[(y + index) * stride + x - 1];
                }
            }
            byte? corner = x > 0 && y > 0 ? pixels[(y - 1) * stride + x - 1] : null;
            if (plane == 0)
            {
                H264IntraPrediction.Luma16x16(mode, y > 0 ? top : [], x > 0 ? left : [], corner, output);
            }
            else
            {
                H264IntraPrediction.Chroma8x8(mode, y > 0 ? top[..8] : [], x > 0 ? left[..8] : [], corner, output);
            }
        }

        public void WriteBlock(int plane, int x, int y, ReadOnlySpan<byte> prediction, ReadOnlySpan<int> residual)
        {
            int stride = Stride(plane);
            Span<byte> pixels = Plane(plane);
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    int index = row * 4 + column;
                    pixels[(y + row) * stride + x + column] = (byte)Math.Clamp((long)prediction[index] + residual[index], 0, 255);
                }
            }
            if (plane == 0)
            {
                Modes[BlockIndex(0, x, y)] &= 127;
            }
        }

        public static (int X, int Y) LumaBlockPosition(int index) =>
            (((index / 4) % 2) * 8 + (index % 2) * 4, (index / 8) * 8 + ((index % 4) / 2) * 4);

        public void Dispose() => storage.Dispose();

        private bool IsAvailable(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height && Modes[BlockIndex(0, x, y)] < 128;
    }
}
