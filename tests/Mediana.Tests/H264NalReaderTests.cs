using System.Buffers.Binary;
using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264NalReaderTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        public void ReturnsBorrowedNalsWithoutConsumingBeyondTheirLengths(int lengthSize)
        {
            byte[] data = new byte[2 * lengthSize + 5];
            WriteLength(data, lengthSize, 3);
            data.AsSpan(lengthSize, 3).Fill(7);
            WriteLength(data.AsSpan(lengthSize + 3), lengthSize, 2);
            data.AsSpan(2 * lengthSize + 3).Fill(8);
            H264NalReader reader = new(data, lengthSize, 2);
            Assert.True(reader.TryRead(out ReadOnlyMemory<byte> first));
            Assert.Equal(new byte[] { 7, 7, 7 }, first.ToArray());
            data[lengthSize] = 9;
            Assert.Equal(9, first.Span[0]);
            Assert.True(reader.TryRead(out ReadOnlyMemory<byte> second));
            Assert.Equal(new byte[] { 8, 8 }, second.ToArray());
            Assert.False(reader.TryRead(out _));
        }

        [Theory]
        [InlineData(new byte[] { 0, 0, 0 })]
        [InlineData(new byte[] { 0, 0, 0, 0 })]
        [InlineData(new byte[] { 0, 0, 0, 2, 1 })]
        [InlineData(new byte[] { 255, 255, 255, 255, 1 })]
        public void RejectsTruncatedEmptyAndOverflowingLengths(byte[] data)
        {
            H264NalReader reader = new(data, 4, 2);
            Assert.Throws<InvalidDataException>(() => reader.TryRead(out _));
        }

        [Fact]
        public void LimitsNalCountAndRejectsReservedLengthSize()
        {
            H264NalReader reader = new(new byte[] { 1, 9, 1, 9 }, 1, 1);
            Assert.True(reader.TryRead(out _));
            Assert.Throws<NotSupportedException>(() => reader.TryRead(out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => new H264NalReader(default, 3, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new H264NalReader(default, 4, 0));
        }

        private static void WriteLength(Span<byte> data, int lengthSize, uint length)
        {
            switch (lengthSize)
            {
                case 1:
                    data[0] = (byte)length;
                    break;
                case 2:
                    BinaryPrimitives.WriteUInt16BigEndian(data, (ushort)length);
                    break;
                case 4:
                    BinaryPrimitives.WriteUInt32BigEndian(data, length);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(lengthSize));
            }
        }
    }
}
