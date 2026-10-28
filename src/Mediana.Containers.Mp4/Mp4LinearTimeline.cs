using Mediana.Core.Timing;

namespace Mediana.Containers.Mp4
{
    internal readonly record struct Mp4LinearTimeline(TimeBase TimeBase, int Scale, Int128 Offset)
    {
        public static Mp4LinearTimeline Identity(TimeBase timeBase) => new(timeBase, 1, 0);

        public static Mp4LinearTimeline Create(TimeBase mediaTimeBase, IReadOnlyList<Mp4Edit> edits)
        {
            if (edits.Count == 0)
            {
                return Identity(mediaTimeBase);
            }
            int mediaIndex = edits.Count == 2 && edits[0].MediaTime == -1 ? 1 : 0;
            if (edits.Count != mediaIndex + 1 || edits[mediaIndex].MediaTime < 0 ||
                edits.Any(edit => edit.Rate != 65536))
            {
                throw new NotSupportedException("Linear movie timestamps require one unit-rate media edit, optionally preceded by one empty edit.");
            }
            Int128 delayNumerator = 0;
            int delayDenominator = 1;
            if (mediaIndex == 1)
            {
                MediaDuration delay = edits[0].Duration;
                delayNumerator = (Int128)delay.Value * delay.TimeBase.Numerator;
                int divisor = GreatestCommonDivisor((int)(delayNumerator % delay.TimeBase.Denominator),
                    delay.TimeBase.Denominator);
                delayNumerator /= divisor;
                delayDenominator = delay.TimeBase.Denominator / divisor;
            }
            int mediaDenominator = mediaTimeBase.Denominator;
            long denominator = (long)mediaDenominator / GreatestCommonDivisor(mediaDenominator, delayDenominator)
                * delayDenominator;
            if (denominator > int.MaxValue)
            {
                throw new NotSupportedException("The exact linear movie time base exceeds the signed 32-bit range.");
            }
            int scale = (int)denominator / mediaDenominator;
            Int128 offset = delayNumerator * (denominator / delayDenominator)
                - (Int128)edits[mediaIndex].MediaTime * scale;
            return new(new TimeBase(1, (int)denominator), scale, offset);
        }

        public MediaTimestamp Map(long value) => new(checked((long)((Int128)value * Scale + Offset)), TimeBase);

        public MediaDuration MapDuration(uint value) => new((long)value * Scale, TimeBase);

        private static int GreatestCommonDivisor(int a, int b)
        {
            while (b != 0)
            {
                (a, b) = (b, a % b);
            }
            return a;
        }
    }
}
