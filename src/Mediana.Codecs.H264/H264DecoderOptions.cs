namespace Mediana.Codecs.H264
{
    public record H264DecoderOptions
    {
        public int MaxPicturePixels { get; init; } = 4096 * 4096;
        public int MaxConfigurationBytes { get; init; } = 64 * 1024;
        public int MaxParameterSetBytes { get; init; } = 16 * 1024;
        public int MaxNalsPerPacket { get; init; } = 1024;
        public int MaxReferenceOperations { get; init; } = 64;

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxPicturePixels);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxConfigurationBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxParameterSetBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxNalsPerPacket);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxReferenceOperations);
        }
    }
}
