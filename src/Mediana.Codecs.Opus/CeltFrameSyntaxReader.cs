// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltFrameSyntaxReader
    {
        public static CeltFrameSyntax Read(ref OpusRangeDecoder decoder, CeltBandMode mode, scoped Span<float> energies,
            scoped Span<int> resolutions, scoped Span<int> boosts, scoped CeltBandAllocation allocation,
            scoped CeltEnergyPrediction prediction = default)
        {
            if (mode.Channels is not (1 or 2)) { throw new ArgumentException("The CELT band mode is uninitialized.", nameof(mode)); }
            ArgumentOutOfRangeException.ThrowIfLessThan(energies.Length, mode.Channels * CeltBandTables.BandCount, nameof(energies));
            ArgumentOutOfRangeException.ThrowIfLessThan(resolutions.Length, mode.EndBand, nameof(resolutions));
            ArgumentOutOfRangeException.ThrowIfLessThan(boosts.Length, mode.EndBand, nameof(boosts));
            CeltFrameFlags flags = CeltFrameFlagsReader.Read(ref decoder, mode);
            prediction.Recover(energies, mode, flags.IntraEnergy);
            CeltEnergyReader.ReadCoarse(ref decoder, energies, mode.Channels, mode.FirstBand, mode.EndBand,
                mode.FrameShift, flags.IntraEnergy);
            CeltTimeFrequencyReader.Read(ref decoder, mode, flags.IsTransient, resolutions);
            CeltAllocationControls controls = CeltAllocationControlsReader.Read(ref decoder, mode, flags.IsTransient, boosts);
            CeltAllocationInfo info = CeltAllocationReader.Read(ref decoder, mode, boosts,
                controls.ShapeBudgetEighthBits, controls.Trim, allocation);
            CeltEnergyReader.ReadFine(ref decoder, energies, allocation.FineBits, mode.Channels, mode.FirstBand, mode.EndBand);
            return new(flags, controls, info);
        }
    }
}
