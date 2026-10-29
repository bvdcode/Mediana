using Mediana.Codecs.Aac;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class AacSpectralToolsTests
    {
        [Fact]
        public void EveryQuantizedMagnitudeMatchesThePowerFormula()
        {
            MediaBufferAllocator allocator = new(65536);
            using AacChannelState channel = Channel(allocator, 11, 100);
            uint noise = 1;
            for (int magnitude = 0; magnitude <= 8191; magnitude++)
            {
                channel.Quantized[0] = magnitude;
                channel.Quantized[1] = -magnitude;
                AacSpectralTools.Reconstruct(channel, null, false, 0, [], ref noise, CancellationToken.None);
                double expected = Math.Pow(magnitude, 4.0 / 3);
                Assert.Equal(expected, channel.Spectrum[0]);
                Assert.Equal(-expected, channel.Spectrum[1]);
            }
        }

        [Fact]
        public void EverySpectralScaleMatchesTheQuarterPowerFormula()
        {
            MediaBufferAllocator allocator = new(65536);
            using AacChannelState channel = Channel(allocator, 11, 0);
            channel.Quantized[0] = 8191;
            channel.Quantized[1] = -8;
            uint noise = 1;
            for (int scale = 0; scale <= 255; scale++)
            {
                channel.Scales[0] = scale;
                AacSpectralTools.Reconstruct(channel, null, false, 0, [], ref noise, CancellationToken.None);
                double gain = Math.Pow(2, 0.25 * (scale - 100));
                Assert.Equal(Math.Pow(8191, 4.0 / 3) * gain, channel.Spectrum[0]);
                Assert.Equal(-Math.Pow(8, 4.0 / 3) * gain, channel.Spectrum[1]);
            }
        }

        [Fact]
        public void InverseQuantizationPreservesSignsAndQuarterPowerScales()
        {
            MediaBufferAllocator allocator = new(65536);
            using AacChannelState channel = Channel(allocator, 1, 104);
            channel.Quantized[0] = 8;
            channel.Quantized[1] = -8;
            uint noise = 1;
            AacSpectralTools.Reconstruct(channel, null, false, 0, [], ref noise, CancellationToken.None);
            Assert.InRange(Math.Abs(channel.Spectrum[0] - 32), 0, 1e-12);
            Assert.InRange(Math.Abs(channel.Spectrum[1] + 32), 0, 1e-12);
            Assert.Equal(0, channel.Spectrum[2]);
        }

        [Theory]
        [InlineData(14, 0, false, -0.5)]
        [InlineData(15, 0, false, 0.5)]
        [InlineData(14, 1, true, 0.5)]
        [InlineData(15, 1, true, -0.5)]
        public void IntensityStereoUsesItsOwnScaleAndExplicitPhase(int book, int mode, bool masked, double expected)
        {
            MediaBufferAllocator allocator = new(65536);
            using AacChannelState left = Channel(allocator, 1, 100);
            using AacChannelState right = Channel(allocator, book, 4);
            left.Quantized[0] = 1;
            byte[] mask = new byte[512];
            mask[0] = masked ? (byte)1 : (byte)0;
            uint noise = 1;
            AacSpectralTools.Reconstruct(left, right, true, mode, mask, ref noise, CancellationToken.None);
            Assert.Equal(1, left.Spectrum[0]);
            Assert.Equal(expected, right.Spectrum[0]);
        }

        [Theory]
        [InlineData(0, 2, 1)]
        [InlineData(1, 3, 1)]
        [InlineData(2, 3, 1)]
        public void MidSideReconstructsBothChannelsWithoutAveraging(int mode, double expectedLeft, double expectedRight)
        {
            MediaBufferAllocator allocator = new(65536);
            using AacChannelState left = Channel(allocator, 1, 104);
            using AacChannelState right = Channel(allocator, 1, 100);
            left.Quantized[0] = 1;
            right.Quantized[0] = 1;
            byte[] mask = new byte[512];
            mask[0] = 1;
            uint noise = 1;
            AacSpectralTools.Reconstruct(left, right, true, mode, mask, ref noise, CancellationToken.None);
            Assert.Equal(expectedLeft, left.Spectrum[0]);
            Assert.Equal(expectedRight, right.Spectrum[0]);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void NoiseEnergyAndStereoCorrelationFollowTheSignalledBands(int mode)
        {
            MediaBufferAllocator allocator = new(65536);
            using AacChannelState left = Channel(allocator, 13, 8);
            using AacChannelState right = Channel(allocator, 13, 12);
            byte[] mask = new byte[512];
            mask[0] = 1;
            uint noise = 1;
            AacSpectralTools.Reconstruct(left, right, true, mode, mask, ref noise, CancellationToken.None);
            double leftEnergy = 0;
            double rightEnergy = 0;
            double difference = 0;
            for (int index = 0; index < 4; index++)
            {
                leftEnergy += left.Spectrum[index] * left.Spectrum[index];
                rightEnergy += right.Spectrum[index] * right.Spectrum[index];
                difference += Math.Abs(right.Spectrum[index] - 2 * left.Spectrum[index]);
            }
            Assert.InRange(Math.Abs(leftEnergy - 16), 0, 1e-12);
            Assert.InRange(Math.Abs(rightEnergy - 64), 0, 1e-12);
            if (mode == 0)
            {
                Assert.True(difference > 1);
            }
            else
            {
                Assert.Equal(0, difference);
            }
        }

        [Theory]
        [InlineData(1, 15, 2)]
        [InlineData(13, 1, 2)]
        [InlineData(1, 13, 1)]
        public void ForbiddenJointStereoCombinationsAreRejected(int leftBook, int rightBook, int mode)
        {
            MediaBufferAllocator allocator = new(65536);
            using AacChannelState left = Channel(allocator, leftBook, 100);
            using AacChannelState right = Channel(allocator, rightBook, 0);
            byte[] mask = new byte[512];
            mask[0] = 1;
            uint noise = 1;
            Assert.Throws<InvalidDataException>(() =>
                AacSpectralTools.Reconstruct(left, right, true, mode, mask, ref noise, CancellationToken.None));
        }

        private static AacChannelState Channel(MediaBufferAllocator allocator, int book, int scale)
        {
            AacChannelState channel = new(allocator)
            {
                Info = new AacWindowInfo(3, 0, 0, 1, [1])
            };
            channel.Books[0] = (byte)book;
            channel.Scales[0] = scale;
            return channel;
        }
    }
}
