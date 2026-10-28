using Mediana.Core.Memory;

namespace Mediana.Containers.MpegTs
{
    internal class AacTransportFraming
    {
        private readonly int frequency;
        private readonly int channels;

        public AacTransportFraming(ReadOnlySpan<byte> configuration)
        {
            if (configuration.Length < 2)
            {
                throw new InvalidDataException("The AAC AudioSpecificConfig is truncated.");
            }
            int objectType = configuration[0] >> 3;
            frequency = (configuration[0] & 7) << 1 | configuration[1] >> 7;
            channels = configuration[1] >> 3 & 15;
            if (objectType != 2 || frequency > 12 || channels is < 1 or > 7 || (configuration[1] & 7) != 0)
            {
                throw new NotSupportedException("ADTS output requires AAC-LC with an indexed sample rate, channel configuration and 1024-sample frames.");
            }
            if (configuration.Length != 2 && !configuration[2..].SequenceEqual(new byte[] { 0x56, 0xE5, 0 }))
            {
                throw new NotSupportedException("The AAC configuration extension is not supported by the TS writer.");
            }
        }

        public MediaBuffer Convert(ReadOnlySpan<byte> data, MediaBufferAllocator allocator)
        {
            if (data.Length is < 1 or > 8184)
            {
                throw new InvalidDataException("The AAC access unit does not fit a single ADTS frame.");
            }
            int size = data.Length + 7;
            using WritableMediaBuffer result = allocator.Allocate(size);
            Span<byte> output = result.Memory.Span;
            output[0] = 0xFF;
            output[1] = 0xF1;
            output[2] = (byte)(0x40 | frequency << 2 | channels >> 2);
            output[3] = (byte)((channels & 3) << 6 | size >> 11);
            output[4] = (byte)(size >> 3);
            output[5] = (byte)((size & 7) << 5 | 0x1F);
            output[6] = 0xFC;
            data.CopyTo(output[7..]);
            return result.Publish();
        }
    }
}
