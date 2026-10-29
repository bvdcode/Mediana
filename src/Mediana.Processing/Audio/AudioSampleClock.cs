using Mediana.Core.Timing;

namespace Mediana.Processing.Audio
{
    internal class AudioSampleClock
    {
        private readonly MediaTimestamp? origin;
        private readonly TimeBase timeBase;
        private readonly long originTicks;
        private readonly int inputStep;
        private readonly int outputStep;

        public AudioSampleClock(MediaTimestamp? origin, int inputRate, int outputRate)
        {
            this.origin = origin;
            if (origin is MediaTimestamp timestamp)
            {
                int denominator = LeastCommonMultiple(LeastCommonMultiple(inputRate, outputRate), timestamp.TimeBase.Denominator);
                timeBase = new TimeBase(1, denominator);
                originTicks = timestamp.Rescale(timeBase, TimestampRounding.TowardZero).Value;
                inputStep = denominator / inputRate;
                outputStep = denominator / outputRate;
            }
        }

        public MediaTimestamp? AtOutput(long position)
        {
            if (origin is null)
            {
                return null;
            }
            return new MediaTimestamp(checked(originTicks + position * outputStep), timeBase);
        }

        public void Validate(MediaTimestamp? timestamp, long position)
        {
            if (origin is null)
            {
                if (timestamp is not null)
                {
                    throw new NotSupportedException("PCM timestamps changed from unknown to known.");
                }
                return;
            }
            MediaTimestamp expected = new(checked(originTicks + position * inputStep), timeBase);
            if (timestamp is null || timestamp.Value != expected)
            {
                throw new InvalidDataException("Resampling requires continuous PCM timestamps.");
            }
        }

        internal static int GreatestCommonDivisor(int a, int b)
        {
            while (b != 0)
            {
                (a, b) = (b, a % b);
            }
            return a;
        }

        private static int LeastCommonMultiple(int a, int b)
        {
            long result = (long)(a / GreatestCommonDivisor(a, b)) * b;
            if (result > int.MaxValue)
            {
                throw new NotSupportedException("The audio rates and timestamp cannot share an exact supported time base.");
            }
            return (int)result;
        }
    }
}
