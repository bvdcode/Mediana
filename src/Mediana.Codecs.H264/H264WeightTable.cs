namespace Mediana.Codecs.H264
{
    internal record H264WeightTable(int LumaDenominator, int ChromaDenominator, IReadOnlyList<H264PredictionWeight> Entries)
    {
        public static (H264WeightTable L0, H264WeightTable? L1) Read(ref H264BitReader bits, int referencesL0, int referencesL1)
        {
            if (referencesL0 is < 1 or > 32 || referencesL1 is < 0 or > 32)
            {
                throw new InvalidDataException("Invalid H.264 prediction weight reference counts.");
            }
            int lumaDenominator = bits.ReadUe(7);
            int chromaDenominator = bits.ReadUe(7);
            H264WeightTable l0 = ReadEntries(ref bits, referencesL0, lumaDenominator, chromaDenominator);
            H264WeightTable? l1 = referencesL1 == 0 ? null : ReadEntries(ref bits, referencesL1, lumaDenominator, chromaDenominator);
            return (l0, l1);
        }

        private static H264WeightTable ReadEntries(ref H264BitReader bits, int referenceCount, int lumaDenominator, int chromaDenominator)
        {
            H264PredictionWeight[] weights = new H264PredictionWeight[referenceCount];
            for (int index = 0; index < referenceCount; index++)
            {
                int lumaWeight = 1 << lumaDenominator;
                int lumaOffset = 0;
                int cbWeight = 1 << chromaDenominator;
                int crWeight = cbWeight;
                int cbOffset = 0;
                int crOffset = 0;
                if (bits.ReadFlag())
                {
                    lumaWeight = bits.ReadSe(-128, 127);
                    lumaOffset = bits.ReadSe(-128, 127);
                }
                if (bits.ReadFlag())
                {
                    cbWeight = bits.ReadSe(-128, 127);
                    cbOffset = bits.ReadSe(-128, 127);
                    crWeight = bits.ReadSe(-128, 127);
                    crOffset = bits.ReadSe(-128, 127);
                }
                weights[index] = new H264PredictionWeight(lumaWeight, lumaOffset, cbWeight, cbOffset, crWeight, crOffset);
            }
            return new H264WeightTable(lumaDenominator, chromaDenominator, Array.AsReadOnly(weights));
        }

        public void Apply(int reference, int plane, Span<byte> samples)
        {
            H264PredictionWeight entry = Entries[reference];
            (int weight, int offset, int denominator) = plane switch
            {
                0 => (entry.LumaWeight, entry.LumaOffset, LumaDenominator),
                1 => (entry.CbWeight, entry.CbOffset, ChromaDenominator),
                2 => (entry.CrWeight, entry.CrOffset, ChromaDenominator),
                _ => throw new ArgumentOutOfRangeException(nameof(plane))
            };
            int rounding = denominator == 0 ? 0 : 1 << (denominator - 1);
            for (int index = 0; index < samples.Length; index++)
            {
                samples[index] = (byte)Math.Clamp(((weight * samples[index] + rounding) >> denominator) + offset, 0, 255);
            }
        }
    }
}
