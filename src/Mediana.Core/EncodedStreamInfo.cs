using Mediana.Core.Timing;

namespace Mediana.Core
{
    /// <summary>Immutable stream configuration. Codec identifiers are independent of implementations.</summary>
    public class EncodedStreamInfo
    {
        public EncodedStreamInfo(int streamIndex, MediaKind kind, string codecId, TimeBase timeBase,
            ReadOnlySpan<byte> configuration = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            ArgumentException.ThrowIfNullOrWhiteSpace(codecId);
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            timeBase.Validate();
            StreamIndex = streamIndex;
            Kind = kind;
            CodecId = codecId;
            TimeBase = timeBase;
            Configuration = configuration.ToArray();
        }

        public int StreamIndex { get; }
        public MediaKind Kind { get; }
        public string CodecId { get; }
        public TimeBase TimeBase { get; }
        public ReadOnlyMemory<byte> Configuration { get; }
    }
}
