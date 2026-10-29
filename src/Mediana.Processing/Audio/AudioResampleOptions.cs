namespace Mediana.Processing.Audio
{
    public record AudioResampleOptions
    {
        public int SamplesPerFrame { get; init; } = 1024;
        public int FilterHalfLength { get; init; } = 16;
        public double CutoffRatio { get; init; } = 0.94;
        public AudioBoundaryMode BoundaryMode { get; init; } = AudioBoundaryMode.Zero;
        public int MaxCoefficientBytes { get; init; } = 8 * 1024 * 1024;

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(SamplesPerFrame);
            ArgumentOutOfRangeException.ThrowIfLessThan(FilterHalfLength, 2);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(FilterHalfLength, 128);
            if (!double.IsFinite(CutoffRatio) || CutoffRatio <= 0 || CutoffRatio > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(CutoffRatio));
            }
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxCoefficientBytes);
            switch (BoundaryMode)
            {
                case AudioBoundaryMode.Zero:
                case AudioBoundaryMode.Reflect:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(BoundaryMode));
            }
        }
    }
}
