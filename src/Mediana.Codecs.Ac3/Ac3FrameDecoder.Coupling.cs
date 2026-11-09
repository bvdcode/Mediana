// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Ac3
{
    internal partial class Ac3FrameDecoder
    {
        private void ReadCoupling(ref Ac3BitReader bits, bool inUse, bool enhanced)
        {
            bool previous = coupling;
            coupling = inUse;
            if (!coupling) { Array.Clear(coupled); Array.Clear(coordinateValid); phaseInUse = false; enhancedCouplingInUse = false; return; }
            bool previousEnhanced = enhancedCouplingInUse;
            enhancedCouplingInUse = enhanced && bits.Flag();
            int count = 0;
            for (int ch = 0; ch < fullChannels; ch++)
            {
                bool inChannel = enhanced && header.ChannelMode == 2 || bits.Flag();
                if (!inChannel || !coupled[ch] || !previous || previousEnhanced != enhancedCouplingInUse) { coordinateValid[ch] = false; }
                coupled[ch] = inChannel; if (inChannel) { count++; }
            }
            if (count < 2) { throw new InvalidDataException("AC-3 coupling requires at least two channels."); }
            if (enhancedCouplingInUse)
            {
                enhancedCoordinates.ReadStrategy(ref bits, spectralExtension.InUse, spectralExtension.BeginCode);
                couplingExponentsRequired = !previous || couplingStart != enhancedCoordinates.StartBin || couplingEnd != enhancedCoordinates.EndBin;
                couplingStart = enhancedCoordinates.StartBin; couplingEnd = enhancedCoordinates.EndBin;
                phaseInUse = false;
                return;
            }
            phaseInUse = header.ChannelMode == 2 && bits.Flag();
            int begin = bits.Read(4);
            int finish = enhanced && spectralExtension.InUse ? (spectralExtension.BeginBin - 37) / 12 : bits.Read(4) + 3;
            if (begin >= finish) { throw new InvalidDataException("The AC-3 coupling frequency range is invalid."); }
            int newStart = 37 + begin * 12, newEnd = 37 + finish * 12;
            couplingExponentsRequired = !previous || newStart != couplingStart || newEnd != couplingEnd;
            couplingStart = newStart; couplingEnd = newEnd; subbands = finish - begin;
            if (!enhanced || bits.Flag())
            {
                couplingMerge[0] = false;
                for (int band = 1; band < subbands; band++) { couplingMerge[band] = bits.Flag(); }
            }
            else if (!previous || previousEnhanced)
            {
                for (int band = 1; band < subbands; band++) { couplingMerge[band] = DefaultCouplingMerge[begin + band] != 0; }
            }
        }

        private void ReadCoordinates(ref Ac3BitReader bits, bool enhanced)
        {
            if (!coupling) { return; }
            if (enhancedCouplingInUse)
            {
                enhancedCoordinates.ReadCoordinates(ref bits, coupled, coordinateValid, fullChannels);
                return;
            }
            Array.Clear(coordinateNew);
            for (int ch = 0; ch < fullChannels; ch++)
            {
                if (!coupled[ch]) { continue; }
                bool present = enhanced && !coordinateValid[ch] || bits.Flag();
                coordinateNew[ch] = present;
                if (!present && !coordinateValid[ch]) { throw new InvalidDataException("AC-3 coupling coordinates cannot be reused before being defined."); }
                if (!present) { continue; }
                int master = bits.Read(2) * 3;
                double coordinate = 0;
                for (int band = 0; band < subbands; band++)
                {
                    if (band == 0 || !couplingMerge[band])
                    {
                        int exponent = bits.Read(4), mantissa = bits.Read(4);
                        coordinate = Math.ScaleB(exponent == 15 ? mantissa / 16.0 : (mantissa + 16) / 32.0, -exponent - master) * 8;
                    }
                    workspace.Coordinates(ch)[band] = coordinate;
                }
                coordinateValid[ch] = true;
            }
            if (header.ChannelMode == 2 && phaseInUse && (coordinateNew[0] || coordinateNew[1]))
            {
                bool negative = false;
                for (int band = 0; band < subbands; band++)
                {
                    if (band == 0 || !couplingMerge[band]) { negative = bits.Flag(); }
                    double coordinate = Math.Abs(workspace.Coordinates(1)[band]);
                    workspace.Coordinates(1)[band] = negative ? -coordinate : coordinate;
                }
            }
        }

        private void ReadRematrix(ref Ac3BitReader bits, int block, bool enhanced)
        {
            if (header.ChannelMode != 2) { return; }
            bool present = enhanced && block == 0 || bits.Flag();
            if (!present && block == 0) { throw new InvalidDataException("The first AC-3 block must define its rematrix strategy."); }
            if (!present) { return; }
            int bands = !coupling ? spectralExtension.InUse && spectralExtension.BeginCode < 2 ? 3 : 4 :
                couplingStart > 61 ? 4 : couplingStart > 37 ? 3 : 2;
            if (enhancedCouplingInUse)
            {
                bands = enhancedCoordinates.BeginCode switch { 0 => 0, 1 => 1, 2 => 2, 3 or 4 => 3, _ => 4 };
            }
            for (int band = 0; band < 4; band++) { rematrix[band] = band < bands && bits.Flag(); }
        }
    }
}
