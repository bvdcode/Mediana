namespace Mediana.Codecs.H264
{
    /// <summary>Reads progressive 4:2:0 transform coefficients in scan order under H.264 clause 7.3.5.3.3.</summary>
    internal static class H264CabacResidual
    {
        public static int Read(ref H264BitReader bits, H264CabacIntraReader reader,
            H264CabacBlockCategory category, int neighbourContext, scoped Span<int> coefficients)
        {
            (int count, int significantOffset, int levelOffset) = category switch
            {
                H264CabacBlockCategory.Intra16LumaDc => (16, 0, 0),
                H264CabacBlockCategory.Intra16LumaAc => (15, 15, 10),
                H264CabacBlockCategory.Luma4x4 => (16, 29, 20),
                H264CabacBlockCategory.ChromaDc => (4, 44, 30),
                H264CabacBlockCategory.ChromaAc => (15, 47, 39),
                _ => throw new ArgumentOutOfRangeException(nameof(category))
            };
            ArgumentOutOfRangeException.ThrowIfNegative(neighbourContext);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(neighbourContext, 3);
            if (coefficients.Length != count)
            {
                throw new ArgumentException("The coefficient count does not match the CABAC block category.", nameof(coefficients));
            }
            coefficients.Clear();
            if (!reader.Read(ref bits, 85 + (int)category * 4 + neighbourContext))
            {
                return 0;
            }
            Span<int> positions = stackalloc int[16];
            int total = 0;
            int last = count - 1;
            for (int index = 0; index < count - 1; index++)
            {
                if (reader.Read(ref bits, 105 + significantOffset + index))
                {
                    if (reader.Read(ref bits, 166 + significantOffset + index))
                    {
                        last = index;
                        break;
                    }
                    positions[total++] = index;
                }
            }
            positions[total++] = last;
            int equalOne = 0;
            int greaterOne = 0;
            for (int index = total - 1; index >= 0; index--)
            {
                int firstContext = greaterOne == 0 ? Math.Min(4, 1 + equalOne) : 0;
                int magnitude = 1;
                if (reader.Read(ref bits, 227 + levelOffset + firstContext))
                {
                    int continuation = 227 + levelOffset + 5 + Math.Min(category == H264CabacBlockCategory.ChromaDc ? 3 : 4, greaterOne);
                    int prefix = 1;
                    while (prefix < 14 && reader.Read(ref bits, continuation))
                    {
                        prefix++;
                    }
                    magnitude = prefix + 1;
                    if (prefix == 14)
                    {
                        magnitude += ReadExpGolomb(ref bits, reader);
                    }
                }
                bool negative = reader.Bypass(ref bits);
                int signed = negative ? -magnitude : magnitude;
                if (signed is < short.MinValue or > short.MaxValue)
                {
                    throw new InvalidDataException("The H.264 CABAC coefficient exceeds the 8-bit transform precision.");
                }
                coefficients[positions[index]] = signed;
                if (magnitude == 1)
                {
                    equalOne++;
                }
                else
                {
                    greaterOne++;
                }
            }
            return total;
        }

        private static int ReadExpGolomb(ref H264BitReader bits, H264CabacIntraReader reader)
        {
            int order = 0;
            int value = 0;
            while (reader.Bypass(ref bits))
            {
                if (order >= 15)
                {
                    throw new InvalidDataException("The H.264 CABAC coefficient suffix exceeds the supported precision.");
                }
                value += 1 << order++;
            }
            return value + (int)reader.Bits(ref bits, order);
        }
    }
}
