// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;

namespace Mediana.Codecs.H264
{
    internal partial class H264Picture
    {
        public Span<byte> ReferenceIndices(H264ReferenceList list) => Metadata.Slice(MotionOffset(list), lumaBlocks);
        public Span<int> MotionX(H264ReferenceList list) => WideMotionComponents(list)[..lumaBlocks];
        public Span<int> MotionY(H264ReferenceList list) => WideMotionComponents(list)[lumaBlocks..];

        public H264StoredMotion MotionVectors(H264ReferenceList list) =>
            new(MotionStorage(list)[lumaBlocks..], lumaBlocks, motionComponentBytes == sizeof(int));

        public H264MotionSample MotionAt(H264ReferenceList list, int x, int y)
        {
            Span<byte> motion = MotionStorage(list);
            if (!IsInCurrentSlice(x, y))
            {
                return new H264MotionSample(false, -1, 0, 0);
            }
            return ReadMotion(motion, x, y);
        }

        public H264MotionSample NeighbourMotionAt(H264ReferenceList list, int currentX, int currentY, int x, int y)
        {
            if (Layout.MacroblockAdaptiveFrameField)
            {
                return AdaptiveNeighbourMotionAt(list, currentX, currentY, x, y);
            }
            return MotionAt(list, x, y);
        }

        private H264MotionSample AdaptiveNeighbourMotionAt(H264ReferenceList list, int currentX, int currentY, int x, int y)
        {
            Span<byte> motion = MotionStorage(list);
            int current = Grid.Address(currentX, currentY);
            (int originX, int originY) = Grid.Position(current);
            H264NeighbourLocation? neighbour = Grid.Neighbour(current, x - originX, y - originY, 16, 16, FirstMacroblock);
            if (neighbour is not H264NeighbourLocation location)
            {
                return new H264MotionSample(false, -1, 0, 0);
            }
            (int neighbourX, int neighbourY) = Grid.Position(location.Address);
            H264MotionSample sample = ReadMotion(motion, neighbourX + location.X, neighbourY + location.Y);
            if (sample.ReferenceIndex < 0 || Grid.IsField(current) == Grid.IsField(location.Address))
            {
                return sample;
            }
            if (Grid.IsField(current))
            {
                return sample with { ReferenceIndex = sample.ReferenceIndex * 2, Y = sample.Y / 2 };
            }
            return sample with { ReferenceIndex = sample.ReferenceIndex / 2, Y = sample.Y * 2 };
        }

        private H264MotionSample ReadMotion(ReadOnlySpan<byte> motion, int x, int y)
        {
            int index = BlockIndex(0, x, y);
            byte mode = Modes[index];
            if (mode == byte.MaxValue)
            {
                return new H264MotionSample(false, -1, 0, 0);
            }
            int reference = motion[index];
            if ((mode & 127) != InterMode || reference == byte.MaxValue)
            {
                return new H264MotionSample(true, -1, 0, 0);
            }
            H264StoredMotion vectors = new(motion[lumaBlocks..], lumaBlocks, motionComponentBytes == sizeof(int));
            (int motionX, int motionY) = vectors.Read(index);
            return new H264MotionSample(true, reference, motionX, motionY);
        }

        public void SetMotion(H264ReferenceList list, int x, int y, int width, int height, int reference, int motionX, int motionY)
        {
            ValidateMotionPartition(x, y, width, height, reference);
            Span<byte> motion = MotionStorage(list);
            Span<byte> indices = motion[..lumaBlocks];
            byte indexValue = reference == -1 ? byte.MaxValue : (byte)reference;
            Span<byte> modes = Modes;
            if (reference == -1)
            {
                motionX = 0;
                motionY = 0;
            }
            int stride = Width / 4;
            int start = BlockIndex(0, x, y);
            int columns = width / 4;
            if (motionComponentBytes == sizeof(int))
            {
                WriteWideMotion(motion[lumaBlocks..], indices, modes, start, stride, columns, height, indexValue, motionX, motionY);
                return;
            }
            if (motionX is < short.MinValue or > short.MaxValue || motionY is < short.MinValue or > short.MaxValue)
            {
                throw new InvalidDataException("The H.264 stored motion vector exceeds its supported range.");
            }
            WriteCompactMotion(motion[lumaBlocks..], indices, modes, start, stride, columns, height, indexValue,
                (short)motionX, (short)motionY);
        }

        private void WriteWideMotion(Span<byte> bytes, Span<byte> indices, Span<byte> modes, int start, int stride,
            int columns, int height, byte indexValue, int motionX, int motionY)
        {
            Span<int> vectors = MemoryMarshal.Cast<byte, int>(bytes);
            Span<int> vectorsX = vectors[..lumaBlocks];
            Span<int> vectorsY = vectors[lumaBlocks..];
            for (int row = 0; row < height; row += 4, start += stride)
            {
                for (int index = start; index < start + columns; index++)
                {
                    modes[index] = InterMode | 128;
                    indices[index] = indexValue;
                    vectorsX[index] = motionX;
                    vectorsY[index] = motionY;
                }
            }
        }

        private void WriteCompactMotion(Span<byte> bytes, Span<byte> indices, Span<byte> modes, int start, int stride,
            int columns, int height, byte indexValue, short motionX, short motionY)
        {
            Span<uint> vectors = MemoryMarshal.Cast<byte, uint>(bytes);
            uint packed = (ushort)motionX | ((uint)(ushort)motionY << 16);
            for (int row = 0; row < height; row += 4, start += stride)
            {
                for (int index = start; index < start + columns; index++)
                {
                    modes[index] = InterMode | 128;
                    indices[index] = indexValue;
                    vectors[index] = packed;
                }
            }
        }

        public void SetReference(H264ReferenceList list, int x, int y, int width, int height, int reference)
        {
            ValidateMotionPartition(x, y, width, height, reference);
            Span<byte> indices = ReferenceIndices(list);
            byte value = reference == -1 ? byte.MaxValue : (byte)reference;
            int start = BlockIndex(0, x, y);
            for (int row = 0; row < height; row += 4, start += Width / 4)
            {
                indices.Slice(start, width / 4).Fill(value);
            }
        }

        private Span<int> WideMotionComponents(H264ReferenceList list)
        {
            if (motionComponentBytes != sizeof(int))
            {
                throw new InvalidOperationException("The H.264 picture stores compact motion vectors.");
            }
            return MemoryMarshal.Cast<byte, int>(MotionStorage(list)[lumaBlocks..]);
        }

        private Span<byte> MotionStorage(H264ReferenceList list) =>
            Metadata.Slice(MotionOffset(list), lumaBlocks * MotionBytesPerBlock);

        private int MotionOffset(H264ReferenceList list)
        {
            int offset = list switch
            {
                H264ReferenceList.L0 => 0,
                H264ReferenceList.L1 when referenceLists == 2 => lumaBlocks * MotionBytesPerBlock,
                H264ReferenceList.L1 => throw new InvalidOperationException("This H.264 picture has no L1 motion storage."),
                _ => throw new ArgumentOutOfRangeException(nameof(list))
            };
            return modesOffset + lumaBlocks + offset;
        }

        private void ValidateMotionPartition(int x, int y, int width, int height, int reference)
        {
            if (x < 0 || y < 0 || width <= 0 || height <= 0 || x > Width - width || y > Height - height
                || ((x | y | width | height) & 3) != 0 || reference is < -1 or > 31)
            {
                throw new ArgumentOutOfRangeException(nameof(reference), "Invalid H.264 motion partition or reference index.");
            }
        }
    }
}
