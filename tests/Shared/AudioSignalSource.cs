using System.Runtime.InteropServices;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    internal class AudioSignalSource(MediaBufferAllocator allocator, int rate, long samples,
        Func<long, int, double> signal, int[] blockSizes, AudioStorageLayout layout = AudioStorageLayout.Interleaved,
        string[]? channels = null)
    {
        private long position;

        public AudioFormat Format { get; } = new(rate, AudioSampleFormat.Float64, layout, channels ?? ["mono"]);
        public long Samples { get; } = samples;
        public int BlocksRead { get; private set; }
        public AudioFrame? LastFrame { get; private set; }
        public Func<long, MediaTimestamp?> Timestamp { get; set; } = index => new MediaTimestamp(index - 7, new TimeBase(1, rate));

        public ValueTask<AudioFrame?> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position == Samples)
            {
                return ValueTask.FromResult<AudioFrame?>(null);
            }
            int count = (int)Math.Min(Samples - position, blockSizes[BlocksRead % blockSizes.Length]);
            using WritableMediaBuffer writable = allocator.Allocate(Format.GetBufferSize(count));
            Span<double> output = MemoryMarshal.Cast<byte, double>(writable.Memory.Span);
            for (int sample = 0; sample < count; sample++)
            {
                for (int channel = 0; channel < Format.Channels.Count; channel++)
                {
                    int index = Format.Layout switch
                    {
                        AudioStorageLayout.Interleaved => sample * Format.Channels.Count + channel,
                        AudioStorageLayout.Planar => channel * count + sample,
                        _ => throw new ArgumentOutOfRangeException(nameof(Format.Layout))
                    };
                    output[index] = signal(position + sample, channel);
                }
            }
            using MediaBuffer buffer = writable.Publish();
            AudioFrame frame = new(Format, count, buffer, Timestamp(position));
            position += count;
            BlocksRead++;
            LastFrame = frame;
            return ValueTask.FromResult<AudioFrame?>(frame);
        }
    }
}
