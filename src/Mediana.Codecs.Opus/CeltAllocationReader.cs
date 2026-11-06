// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal ref struct CeltAllocationReader
    {
        private readonly CeltBandMode mode;
        private readonly ReadOnlySpan<int> boosts;
        private readonly Span<int> caps;
        private readonly Span<int> thresholds;
        private readonly Span<int> tilt;
        private readonly Span<int> lower;
        private readonly Span<int> delta;
        private int total;
        private int skipReserve;
        private int intensityReserve;
        private int dualReserve;
        private int skipThrough;
        private readonly int trim;

        private CeltAllocationReader(CeltBandMode mode, ReadOnlySpan<int> boosts, Span<int> scratch, int total, int trim)
        {
            this.mode = mode;
            this.boosts = boosts;
            this.total = Math.Max(0, total);
            this.trim = trim;
            const int bands = CeltBandTables.BandCount;
            caps = scratch[..bands];
            thresholds = scratch.Slice(bands, bands);
            tilt = scratch.Slice(2 * bands, bands);
            lower = scratch.Slice(3 * bands, bands);
            delta = scratch.Slice(4 * bands, bands);
            skipThrough = mode.FirstBand;
        }

        public static CeltAllocationInfo Read(ref OpusRangeDecoder decoder, CeltBandMode mode,
            scoped ReadOnlySpan<int> boosts, int availableEighthBits, int trim, scoped CeltBandAllocation allocation)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(boosts.Length, mode.EndBand, nameof(boosts));
            ArgumentOutOfRangeException.ThrowIfNegative(trim);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(trim, 10);
            if (mode.Channels is not (1 or 2)) { throw new ArgumentException("The CELT band mode is uninitialized.", nameof(mode)); }
            foreach (int boost in boosts[mode.FirstBand..mode.EndBand])
            {
                ArgumentOutOfRangeException.ThrowIfNegative(boost, nameof(boosts));
                ArgumentOutOfRangeException.ThrowIfGreaterThan(boost, decoder.FrameBitCount * 8, nameof(boosts));
            }
            ArgumentOutOfRangeException.ThrowIfGreaterThan(availableEighthBits, decoder.FrameBitCount * 8, nameof(availableEighthBits));
            Span<int> scratch = stackalloc int[5 * CeltBandTables.BandCount];
            CeltAllocationReader reader = new(mode, boosts, scratch, availableEighthBits, trim);
            reader.Prepare();
            int used = reader.Interpolate(allocation.ShapeEighthBits);
            (int coded, int remaining, int reservation) = ReadSkip(reader, ref decoder, allocation.ShapeEighthBits, ref used);
            reader.total = remaining;
            reader.intensityReserve = reservation;
            (int intensity, bool dual, int updatedTotal) = ReadStereo(reader, ref decoder, coded);
            reader.total = updatedTotal;
            reader.Distribute(allocation.ShapeEighthBits, coded, used);
            int balance = CeltFineAllocation.Apply(mode, allocation, reader.caps, coded, intensity, dual);
            return new(coded, intensity, dual, balance);
        }

        private void Prepare()
        {
            if (total >= 8) { skipReserve = 8; total -= 8; }
            if (mode.Channels == 2)
            {
                int needed = CeltBandTables.StereoReservationEighths[mode.EndBand - mode.FirstBand];
                if (needed <= total)
                {
                    intensityReserve = needed;
                    total -= needed;
                    if (total >= 8) { dualReserve = 8; total -= 8; }
                }
            }
            for (int band = mode.FirstBand; band < mode.EndBand; band++)
            {
                int width = CeltBandTables.Width(band);
                caps[band] = CeltBandTables.MaximumAllocation(mode, band);
                thresholds[band] = Math.Max(mode.Channels * 8, 3 * (width << mode.FrameShift) / 2);
                tilt[band] = mode.Channels * width * (trim - 5 - mode.FrameShift) *
                    (mode.EndBand - band - 1) * (1 << (mode.FrameShift + 3)) >> 6;
                if ((width << mode.FrameShift) == 1) { tilt[band] -= mode.Channels * 8; }
            }
            int firstTooLarge = 1;
            int upperQuality = CeltBandTables.QualityCount;
            while (firstTooLarge < upperQuality)
            {
                int quality = (firstTooLarge + upperQuality) / 2;
                if (QualityCost(quality) <= total) { firstTooLarge = quality + 1; }
                else { upperQuality = quality; }
            }
            int selected = firstTooLarge - 1;
            for (int band = mode.FirstBand; band < mode.EndBand; band++)
            {
                int low = WithTilt(CeltBandTables.StaticAllocation(mode, band, selected), band);
                int high;
                if (firstTooLarge == CeltBandTables.QualityCount) { high = caps[band]; }
                else { high = CeltBandTables.StaticAllocation(mode, band, firstTooLarge); }
                high = WithTilt(high, band) + boosts[band];
                if (selected > 0) { low += boosts[band]; }
                lower[band] = low;
                delta[band] = Math.Max(0, high - low);
                if (boosts[band] > 0) { skipThrough = band; }
            }
        }

        private readonly int WithTilt(int value, int band)
        {
            if (value == 0) { return 0; }
            return Math.Max(0, value + tilt[band]);
        }

        private readonly int QualityCost(int quality)
        {
            int sum = 0;
            bool active = false;
            for (int band = mode.EndBand - 1; band >= mode.FirstBand; band--)
            {
                int value = WithTilt(CeltBandTables.StaticAllocation(mode, band, quality), band) + boosts[band];
                if (active || value >= thresholds[band])
                {
                    active = true;
                    sum += Math.Min(caps[band], value);
                }
                else if (value >= mode.Channels * 8) { sum += mode.Channels * 8; }
            }
            return sum;
        }

        private readonly int EvaluateFraction(int fraction, Span<int> destination)
        {
            int sum = 0;
            bool active = false;
            for (int band = mode.EndBand - 1; band >= mode.FirstBand; band--)
            {
                int value = lower[band] + (fraction * delta[band] >> 6);
                if (active || value >= thresholds[band]) { active = true; }
                else
                {
                    value = Math.Min(value, mode.Channels * 8);
                    if (value < mode.Channels * 8) { value = 0; }
                }
                value = Math.Min(value, caps[band]);
                if (!destination.IsEmpty) { destination[band] = value; }
                sum += value;
            }
            return sum;
        }

        private readonly int Interpolate(Span<int> shape)
        {
            int low = 0, high = 64;
            for (int bit = 0; bit < 6; bit++)
            {
                int fraction = (low + high) / 2;
                if (EvaluateFraction(fraction, []) <= total) { low = fraction; }
                else { high = fraction; }
            }
            return EvaluateFraction(low, shape);
        }

        private static (int Coded, int Total, int IntensityReserve) ReadSkip(scoped CeltAllocationReader reader,
            ref OpusRangeDecoder decoder, scoped Span<int> shape, ref int used)
        {
            CeltBandMode mode = reader.mode;
            for (int coded = mode.EndBand; ; coded--)
            {
                int band = coded - 1;
                if (band <= reader.skipThrough) { return (coded, reader.total + reader.skipReserve, reader.intensityReserve); }
                int width = CeltBandTables.Width(band);
                int count = CeltBandTables.Boundaries[coded] - CeltBandTables.Boundaries[mode.FirstBand];
                int remaining = reader.total - used;
                int equal = remaining / count;
                int partial = Math.Max(0, remaining % count -
                    (CeltBandTables.Boundaries[band] - CeltBandTables.Boundaries[mode.FirstBand]));
                int available = shape[band] + equal * width + partial;
                if (available >= Math.Max(reader.thresholds[band], mode.Channels * 8 + 8))
                {
                    if (decoder.ReadBit(1)) { return (coded, reader.total, reader.intensityReserve); }
                    used += 8;
                    available -= 8;
                }
                used -= shape[band] + reader.intensityReserve;
                if (reader.intensityReserve > 0)
                {
                    reader.intensityReserve = CeltBandTables.StereoReservationEighths[band - mode.FirstBand];
                }
                used += reader.intensityReserve;
                shape[band] = 0;
                if (available >= mode.Channels * 8)
                {
                    shape[band] = mode.Channels * 8;
                    used += shape[band];
                }
            }
        }

        private static (int Intensity, bool Dual, int Total) ReadStereo(scoped CeltAllocationReader reader,
            ref OpusRangeDecoder decoder, int coded)
        {
            int intensity = 0;
            if (reader.intensityReserve > 0)
            {
                intensity = reader.mode.FirstBand + (int)decoder.ReadUniform((uint)(coded + 1 - reader.mode.FirstBand));
            }
            if (intensity <= reader.mode.FirstBand) { reader.total += reader.dualReserve; reader.dualReserve = 0; }
            bool dual = false;
            if (reader.dualReserve > 0) { dual = decoder.ReadBit(1); }
            return (intensity, dual, reader.total);
        }

        private readonly void Distribute(Span<int> shape, int coded, int used)
        {
            int count = CeltBandTables.Boundaries[coded] - CeltBandTables.Boundaries[mode.FirstBand];
            int equal = (total - used) / count;
            int leftover = (total - used) % count;
            for (int band = mode.FirstBand; band < coded; band++)
            {
                int width = CeltBandTables.Width(band);
                int extra = Math.Min(leftover, width);
                shape[band] += equal * width + extra;
                leftover -= extra;
            }
        }
    }
}
