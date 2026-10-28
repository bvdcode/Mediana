using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Core
{
    /// <summary>A compressed packet with its own lease. Packet boundaries need not equal frame boundaries.</summary>
    public class MediaPacket : IDisposable
    {
        private readonly MediaBuffer buffer;

        public MediaPacket(int streamIndex, MediaBuffer buffer, MediaTimestamp? presentationTime = null,
            MediaTimestamp? decodeTime = null, MediaDuration? duration = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            ArgumentNullException.ThrowIfNull(buffer);
            presentationTime?.TimeBase.Validate();
            decodeTime?.TimeBase.Validate();
            duration?.TimeBase.Validate();
            StreamIndex = streamIndex;
            PresentationTime = presentationTime;
            DecodeTime = decodeTime;
            Duration = duration;
            this.buffer = buffer.Retain();
        }

        public int StreamIndex { get; }
        public MediaTimestamp? PresentationTime { get; }
        public MediaTimestamp? DecodeTime { get; }
        public MediaDuration? Duration { get; }
        public ReadOnlyMemory<byte> Data => buffer.Memory;
        public MediaPacket Retain() => new(StreamIndex, buffer, PresentationTime, DecodeTime, Duration);
        public void Dispose() => buffer.Dispose();
    }
}
