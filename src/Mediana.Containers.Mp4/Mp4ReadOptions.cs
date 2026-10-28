namespace Mediana.Containers.Mp4
{
    public record Mp4ReadOptions
    {
        public Mp4TimestampMode TimestampMode { get; init; } = Mp4TimestampMode.Media;
        public int MaxMetadataBytes { get; init; } = 32 * 1024 * 1024;
        public int MaxCodecConfigurationBytes { get; init; } = 64 * 1024;
        public int MaxBoxes { get; init; } = 100_000;
        public int MaxTracks { get; init; } = 128;
        public int MaxSamplesPerTrack { get; init; } = 10_000_000;
        public int MaxEditsPerTrack { get; init; } = 1024;

        internal void Validate()
        {
            if (!Enum.IsDefined(TimestampMode))
            {
                throw new ArgumentOutOfRangeException(nameof(TimestampMode));
            }
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxMetadataBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxCodecConfigurationBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxBoxes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTracks);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxSamplesPerTrack);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEditsPerTrack);
        }
    }
}
