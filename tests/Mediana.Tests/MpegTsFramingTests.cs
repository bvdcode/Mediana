using Mediana.Containers.MpegTs;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class MpegTsFramingTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        public void AvcConvertsAllLengthSizesAndAddsAudAndIdrParameters(int lengthSize)
        {
            AvcTransportFraming framing = new(Configuration(lengthSize));
            byte[] input = [.. Length(2, lengthSize), 0x65, 0x88];
            MediaBufferAllocator allocator = new(1024);
            using (MediaBuffer output = framing.Convert(input, allocator, 1024, out bool idr))
            {
                Assert.True(idr);
                Assert.Equal(Convert.FromHexString("0000000109F00000000167420000000168CE000000016588"), output.Memory.ToArray());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void AvcReplacesAudAndPreservesNonIdrDataWithoutParameterInjection()
        {
            AvcTransportFraming framing = new(Configuration(1));
            MediaBufferAllocator allocator = new(1024);
            using (MediaBuffer output = framing.Convert([2, 9, 0x10, 2, 0x41, 0x9A], allocator, 1024, out bool idr))
            {
                Assert.False(idr);
                Assert.Equal(Convert.FromHexString("0000000109F000000001419A"), output.Memory.ToArray());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(new byte[] { })]
        [InlineData(new byte[] { 0 })]
        [InlineData(new byte[] { 3, 0x65 })]
        [InlineData(new byte[] { 1, 0xE5 })]
        [InlineData(new byte[] { 1, 0x65, 1, 0x41 })]
        [InlineData(new byte[] { 1, 6 })]
        public void InvalidAvcAccessUnitsDoNotAllocateOutput(byte[] input)
        {
            AvcTransportFraming framing = new(Configuration(1));
            MediaBufferAllocator allocator = new(1024);
            Assert.Throws<InvalidDataException>(() => framing.Convert(input, allocator, 1024, out _));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void AvcRejectsParameterChangesAndEnforcesOutputAndAllocationBudgets()
        {
            AvcTransportFraming framing = new(Configuration(1));
            MediaBufferAllocator allocator = new(1024);
            Assert.Throws<NotSupportedException>(() => framing.Convert([2, 0x67, 0x43, 1, 0x65], allocator, 1024, out _));
            Assert.Throws<NotSupportedException>(() => framing.Convert([2, 0x65, 0x88], allocator, 23, out _));
            Assert.Equal(0, allocator.AllocatedBytes);
            MediaBufferAllocator small = new(23);
            Assert.Throws<MediaMemoryLimitException>(() => framing.Convert([2, 0x65, 0x88], small, 1024, out _));
            Assert.Equal(0, small.AllocatedBytes);
        }

        [Theory]
        [InlineData("1188", "FFF14C40015FFC010203")]
        [InlineData("118856E500", "FFF14C40015FFC010203")]
        [InlineData("1210", "FFF15080015FFC010203")]
        public void AacLcFramingMatchesLiteralAdtsHeader(string configuration, string expected)
        {
            AacTransportFraming framing = new(Convert.FromHexString(configuration));
            MediaBufferAllocator allocator = new(1024);
            using (MediaBuffer output = framing.Convert([1, 2, 3], allocator))
            {
                Assert.Equal(Convert.FromHexString(expected), output.Memory.ToArray());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData("2988")]
        [InlineData("118C")]
        [InlineData("1180")]
        [InlineData("118856E580")]
        [InlineData("118800")]
        public void AacRejectsUnsupportedObjectTypesFrameSizesAndExtensions(string configuration) =>
            Assert.Throws<NotSupportedException>(() => new AacTransportFraming(Convert.FromHexString(configuration)));

        [Fact]
        public void AdtsLengthLimitIsExactAndFailuresReleaseStorage()
        {
            AacTransportFraming framing = new([0x11, 0x88]);
            MediaBufferAllocator allocator = new(8191);
            using (MediaBuffer output = framing.Convert(new byte[8184], allocator))
            {
                Assert.Equal(8191, output.Memory.Length);
                Assert.Equal(0xFF, output.Memory.Span[4]);
                Assert.Equal(0xFF, output.Memory.Span[5]);
            }
            Assert.Throws<InvalidDataException>(() => framing.Convert([], allocator));
            Assert.Throws<InvalidDataException>(() => framing.Convert(new byte[8185], allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static byte[] Configuration(int lengthSize) =>
            [1, 66, 0, 30, (byte)(0xFC | lengthSize - 1), 0xE1, 0, 2, 0x67, 0x42, 1, 0, 2, 0x68, 0xCE];

        private static byte[] Length(int length, int size) =>
            Enumerable.Range(0, size).Select(index => (byte)(length >> (size - index - 1) * 8)).ToArray();
    }
}
