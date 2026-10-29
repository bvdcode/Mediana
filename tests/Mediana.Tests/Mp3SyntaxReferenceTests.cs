using Mediana.Codecs.Mp3;
using Mediana.Containers.Mp3;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    public class Mp3SyntaxReferenceTests
    {
        [Fact]
        public async Task AllSideFieldsAndAssembledMainDataMatchIndependentCompleteStreams()
        {
            await Mp3SyntaxTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp3"));
        }

        [Theory]
        [InlineData(48000, 1)]
        [InlineData(48000, 2)]
        [InlineData(24000, 1)]
        [InlineData(24000, 2)]
        [InlineData(8000, 1)]
        [InlineData(8000, 2)]
        public async Task EveryProtectedReferenceFrameRejectsSideInfoCorruption(int rate, int channels)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Mp3", $"crc-{rate}-{channels}.mp3");
            await using StreamByteSource source = new(File.OpenRead(path));
            MediaBufferAllocator allocator = new(128 * 1024);
            await using (Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator))
            {
                while (true)
                {
                    using MediaPacket? packet = await reader.ReadPacketAsync();
                    if (packet is null)
                    {
                        break;
                    }
                    byte[] damaged = packet.Data.ToArray();
                    damaged[6] ^= 128;
                    InvalidDataException exception = Assert.Throws<InvalidDataException>(() => new Mp3FrameInfo().Read(damaged));
                    Assert.Contains("CRC", exception.Message);
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
