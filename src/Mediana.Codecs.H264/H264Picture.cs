using Mediana.Core.Memory;
using System.Runtime.InteropServices;

namespace Mediana.Codecs.H264
{
    internal class H264Picture : IDisposable
    {
        public const byte InterMode = 9;
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
            storage = allocator.Allocate(checked(lumaBytes + chromaBytes * 2 + lumaBlocks * 11 + chromaBlocks * 2 + macroblocks));
            Modes.Fill(byte.MaxValue);
            ReferenceIndices.Fill(byte.MaxValue);
        }

        public H264SequenceParameterSet Sequence { get; }
        public int Width { get; }
        public int Height { get; }
        private int ModesOffset => lumaBytes + chromaBytes * 2 + lumaBlocks + chromaBlocks * 2;
        public Span<byte> Modes => storage.Memory.Span.Slice(ModesOffset, lumaBlocks);
        public Span<byte> ReferenceIndices => storage.Memory.Span.Slice(ModesOffset + lumaBlocks, lumaBlocks);
        public Span<int> MotionX => MemoryMarshal.Cast<byte, int>(storage.Memory.Span.Slice(ModesOffset + lumaBlocks * 2, lumaBlocks * 4));
        public Span<int> MotionY => MemoryMarshal.Cast<byte, int>(storage.Memory.Span.Slice(ModesOffset + lumaBlocks * 6, lumaBlocks * 4));
        public Span<byte> Quantizers => storage.Memory.Span[(ModesOffset + lumaBlocks * 10)..];

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

        public int PredictedMode(int x, int y, bool constrained = false)
        {
            if (x == 0 || y == 0)
            {
                return 2;
            }
            int index = BlockIndex(0, x, y);
            int left = Modes[index - 1] & 127;
            int top = Modes[index - Width / 4] & 127;
            if (left == 127 || top == 127 || (constrained && (left == InterMode || top == InterMode)))
            {
                return 2;
            }
            return Math.Min(left == InterMode ? 2 : left, top == InterMode ? 2 : top);
        }

        public void SetLumaMode(int x, int y, int mode) => Modes[BlockIndex(0, x, y)] = (byte)(mode | 128);

        public void PredictLuma4x4(int x, int y, int mode, Span<byte> output, bool constrained = false)
        {
            Span<byte> top = stackalloc byte[8];
            Span<byte> left = stackalloc byte[4];
            Span<byte> pixels = Plane(0);
            bool hasTop = IsAvailable(x, y - 1, constrained);
            bool hasLeft = IsAvailable(x - 1, y, constrained);
            if (hasTop)
            {
                for (int index = 0; index < 8; index++)
                {
                    top[index] = IsAvailable(x + index, y - 1, constrained) ? pixels[(y - 1) * Width + x + index] : top[3];
                }
            }
            if (hasLeft)
            {
                for (int index = 0; index < 4; index++)
                {
                    left[index] = pixels[(y + index) * Width + x - 1];
                }
            }
            byte? corner = IsAvailable(x - 1, y - 1, constrained) ? pixels[(y - 1) * Width + x - 1] : null;
            H264IntraPrediction.Luma4x4(mode, hasTop ? top : [], hasLeft ? left : [], corner, output);
        }

        public void PredictMacroblock(int plane, int x, int y, int mode, Span<byte> output, bool constrained = false)
        {
            int size = plane == 0 ? 16 : 8;
            int stride = Stride(plane);
            Span<byte> pixels = Plane(plane);
            Span<byte> top = stackalloc byte[16];
            Span<byte> left = stackalloc byte[16];
            int scale = plane == 0 ? 1 : 2;
            bool hasTop = IsAvailable(x * scale, y * scale - 1, constrained);
            bool hasLeft = IsAvailable(x * scale - 1, y * scale, constrained);
            if (hasTop)
            {
                pixels.Slice((y - 1) * stride + x, size).CopyTo(top);
            }
            if (hasLeft)
            {
                for (int index = 0; index < size; index++)
                {
                    left[index] = pixels[(y + index) * stride + x - 1];
                }
            }
            byte? corner = IsAvailable(x * scale - 1, y * scale - 1, constrained) ? pixels[(y - 1) * stride + x - 1] : null;
            if (plane == 0)
            {
                H264IntraPrediction.Luma16x16(mode, hasTop ? top : [], hasLeft ? left : [], corner, output);
            }
            else
            {
                H264IntraPrediction.Chroma8x8(mode, hasTop ? top[..8] : [], hasLeft ? left[..8] : [], corner, output);
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

        public H264MotionSample MotionAt(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
            {
                return new H264MotionSample(false, -1, 0, 0);
            }
            int index = BlockIndex(0, x, y);
            if (Modes[index] == byte.MaxValue)
            {
                return new H264MotionSample(false, -1, 0, 0);
            }
            return (Modes[index] & 127) == InterMode
                ? new H264MotionSample(true, ReferenceIndices[index], MotionX[index], MotionY[index])
                : new H264MotionSample(true, -1, 0, 0);
        }

        public void SetMotion(int x, int y, int width, int height, int reference, int motionX, int motionY)
        {
            for (int row = y; row < y + height; row += 4)
            {
                for (int column = x; column < x + width; column += 4)
                {
                    int index = BlockIndex(0, column, row);
                    Modes[index] = InterMode | 128;
                    ReferenceIndices[index] = checked((byte)reference);
                    MotionX[index] = motionX;
                    MotionY[index] = motionY;
                }
            }
        }

        public void SetReference(int x, int y, int width, int height, int reference)
        {
            for (int row = y; row < y + height; row += 4)
            {
                for (int column = x; column < x + width; column += 4)
                {
                    ReferenceIndices[BlockIndex(0, column, row)] = checked((byte)reference);
                }
            }
        }

        private bool IsAvailable(int x, int y, bool constrained) => x >= 0 && x < Width && y >= 0 && y < Height &&
            Modes[BlockIndex(0, x, y)] < 128 && (!constrained || Modes[BlockIndex(0, x, y)] != InterMode);
    }
}
