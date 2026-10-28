namespace Mediana.Core.Timing
{
    public readonly record struct MediaDuration
    {
        public MediaDuration(long value, TimeBase timeBase)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            timeBase.Validate();
            Value = value;
            TimeBase = timeBase;
        }

        public long Value { get; }
        public TimeBase TimeBase { get; }
    }
}
