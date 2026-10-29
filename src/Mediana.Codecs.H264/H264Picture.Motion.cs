using System.Runtime.InteropServices;

namespace Mediana.Codecs.H264
{
    internal partial class H264Picture
    {
        public Span<byte> ReferenceIndices(H264ReferenceList list) => storage.Memory.Span.Slice(MotionOffset(list), lumaBlocks);
        public Span<int> MotionX(H264ReferenceList list) => MemoryMarshal.Cast<byte, int>(
            storage.Memory.Span.Slice(MotionOffset(list) + lumaBlocks, lumaBlocks * sizeof(int)));
        public Span<int> MotionY(H264ReferenceList list) => MemoryMarshal.Cast<byte, int>(
            storage.Memory.Span.Slice(MotionOffset(list) + lumaBlocks * (1 + sizeof(int)), lumaBlocks * sizeof(int)));

        public H264MotionSample MotionAt(H264ReferenceList list, int x, int y)
        {
            Span<byte> indices = ReferenceIndices(list);
            if (x < 0 || x >= Width || y < 0 || y >= Height)
            {
                return new H264MotionSample(false, -1, 0, 0);
            }
            int index = BlockIndex(0, x, y);
            if (Modes[index] == byte.MaxValue)
            {
                return new H264MotionSample(false, -1, 0, 0);
            }
            if ((Modes[index] & 127) != InterMode || indices[index] == byte.MaxValue)
            {
                return new H264MotionSample(true, -1, 0, 0);
            }
            return new H264MotionSample(true, indices[index], MotionX(list)[index], MotionY(list)[index]);
        }

        public void SetMotion(H264ReferenceList list, int x, int y, int width, int height, int reference, int motionX, int motionY)
        {
            ValidateMotionPartition(x, y, width, height, reference);
            Span<byte> indices = ReferenceIndices(list);
            Span<int> vectorsX = MotionX(list);
            Span<int> vectorsY = MotionY(list);
            byte indexValue = reference == -1 ? byte.MaxValue : (byte)reference;
            for (int row = y; row < y + height; row += 4)
            {
                for (int column = x; column < x + width; column += 4)
                {
                    int index = BlockIndex(0, column, row);
                    Modes[index] = InterMode | 128;
                    indices[index] = indexValue;
                    vectorsX[index] = reference == -1 ? 0 : motionX;
                    vectorsY[index] = reference == -1 ? 0 : motionY;
                }
            }
        }

        public void SetReference(H264ReferenceList list, int x, int y, int width, int height, int reference)
        {
            ValidateMotionPartition(x, y, width, height, reference);
            Span<byte> indices = ReferenceIndices(list);
            byte value = reference == -1 ? byte.MaxValue : (byte)reference;
            for (int row = y; row < y + height; row += 4)
            {
                for (int column = x; column < x + width; column += 4)
                {
                    indices[BlockIndex(0, column, row)] = value;
                }
            }
        }

        private int MotionOffset(H264ReferenceList list)
        {
            int offset = list switch
            {
                H264ReferenceList.L0 => 0,
                H264ReferenceList.L1 when referenceLists == 2 => lumaBlocks * MotionBytesPerBlock,
                H264ReferenceList.L1 => throw new InvalidOperationException("This H.264 picture has no L1 motion storage."),
                _ => throw new ArgumentOutOfRangeException(nameof(list))
            };
            return ModesOffset + lumaBlocks + offset;
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
