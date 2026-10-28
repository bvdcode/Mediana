using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class AudioTests
    {
        [Fact]
        public void PlanarAudioPreservesChannelOrderAndSampleDuration()
        {
            MediaBufferAllocator allocator = new(16);
            AudioFormat format = new(48000, AudioSampleFormat.Float32, AudioStorageLayout.Planar, "front-left", "front-right");
            using WritableMediaBuffer writable = allocator.Allocate(format.GetBufferSize(2));
            writable.Memory.Span[..8].Fill(1);
            writable.Memory.Span[8..].Fill(2);
            using MediaBuffer buffer = writable.Publish();
            using AudioFrame frame = new(format, 2, buffer);
            Assert.Equal(new[] { "front-left", "front-right" }, format.Channels);
            Assert.Equal(8, frame.GetPlane(1).Length);
            Assert.All(frame.GetPlane(1).ToArray(), value => Assert.Equal(2, value));
            Assert.Equal(new MediaDuration(2, new TimeBase(1, 48000)), frame.Duration);
            using AudioFrame retained = frame.Retain();
            frame.Dispose();
            Assert.Equal(1, retained.GetPlane(0).Span[0]);
        }

        [Fact]
        public void InvalidLayoutsAndBufferSizesAreRejected()
        {
            Assert.Throws<ArgumentException>(() =>
                new AudioFormat(48000, AudioSampleFormat.Float32, AudioStorageLayout.Planar, "left", "left"));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AudioFormat(48000, (AudioSampleFormat)99, AudioStorageLayout.Planar, "mono"));
            AudioFormat format = new(44100, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, "left", "right");
            MediaBufferAllocator allocator = new(4);
            using WritableMediaBuffer writable = allocator.Allocate(3);
            using MediaBuffer buffer = writable.Publish();
            Assert.Throws<ArgumentException>(() => new AudioFrame(format, 1, buffer));
            Assert.Throws<OverflowException>(() => format.GetBufferSize(int.MaxValue));
        }
    }
}
