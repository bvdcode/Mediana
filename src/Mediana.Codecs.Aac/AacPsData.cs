// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal class AacPsData
    {
        private readonly AacPsEnvelope previous = new();
        private int? previousIntensityMode;
        private int? previousCoherenceMode;
        private bool independent;

        public AacPsHeader? Header { get; private set; }
        public AacPsEnvelope[] Envelopes { get; } = Enumerable.Range(0, 5).Select(_ => new AacPsEnvelope()).ToArray();
        public int EnvelopeCount { get; private set; }
        public int TransmittedEnvelopeCount { get; private set; }
        public bool PhaseEnabled { get; private set; }
        public bool Independent { get; private set; }

        public void Read(ref AacBitReader bits)
        {
            bool headerPresent = bits.Read(1) != 0;
            if (headerPresent)
            {
                Header = AacPsHeader.Read(ref bits);
            }
            if (Header is null)
            {
                Independent = false;
                bits.Skip(bits.Remaining);
                return;
            }
            foreach (AacPsEnvelope envelope in Envelopes)
            {
                envelope.Clear();
            }
            independent = headerPresent;
            bool variable = bits.Read(1) != 0;
            int index = bits.Read(2);
            int count = variable ? index + 1 : index switch
            {
                0 => 0,
                1 => 1,
                2 => 2,
                3 => 4,
                _ => throw new InvalidDataException("The PS envelope count is invalid.")
            };
            TransmittedEnvelopeCount = count;
            int end = 0;
            for (int envelope = 0; envelope < count; envelope++)
            {
                int next = variable ? bits.Read(5) + 1 : (envelope + 1) * 32 / count;
                if (next <= end)
                {
                    throw new InvalidDataException("The PS envelope boundaries are not increasing.");
                }
                Envelopes[envelope].EndSlot = end = next;
            }
            ReadIntensity(ref bits, count);
            ReadCoherence(ref bits, count);
            PhaseEnabled = false;
            if (Header.ExtensionEnabled)
            {
                ReadExtensions(ref bits, count);
            }
            Independent = independent && count != 0;
            EnvelopeCount = count;
            if (count == 0)
            {
                Envelopes[0].CopyFrom(previous);
                if (Header.IntensityMode is null)
                {
                    Array.Clear(Envelopes[0].Intensity);
                }
                if (Header.CoherenceMode is null)
                {
                    Array.Clear(Envelopes[0].Coherence);
                }
                if (!PhaseEnabled)
                {
                    Array.Clear(Envelopes[0].PhaseDifference);
                    Array.Clear(Envelopes[0].OverallPhase);
                }
                Envelopes[0].EndSlot = 32;
                EnvelopeCount = 1;
            }
            else
            {
                previous.CopyFrom(Envelopes[count - 1]);
                previousIntensityMode = Header.IntensityMode;
                previousCoherenceMode = Header.CoherenceMode;
                if (end < 32)
                {
                    Envelopes[count].CopyFrom(previous);
                    Envelopes[count].EndSlot = 32;
                    EnvelopeCount++;
                }
            }
        }

        private void ReadIntensity(ref AacBitReader bits, int count)
        {
            if (Header?.IntensityMode is not int mode)
            {
                return;
            }
            int bands = AacPsHeader.ParameterCount(mode);
            int limit = mode < 3 ? 7 : 15;
            for (int envelope = 0; envelope < count; envelope++)
            {
                bool time = ReadDirection(ref bits, envelope, mode, previousIntensityMode);
                AacPsCodebook book = (mode < 3, time) switch
                {
                    (true, false) => AacPsCodebook.CoarseFrequency,
                    (true, true) => AacPsCodebook.CoarseTime,
                    (false, false) => AacPsCodebook.FineFrequency,
                    (false, true) => AacPsCodebook.FineTime
                };
                ReadParameters(ref bits, Envelopes[envelope].Intensity,
                    envelope == 0 ? previous.Intensity : Envelopes[envelope - 1].Intensity, bands, time, book, -limit, limit, false);
            }
        }

        private void ReadCoherence(ref AacBitReader bits, int count)
        {
            if (Header?.CoherenceMode is not int mode)
            {
                return;
            }
            int bands = AacPsHeader.ParameterCount(mode);
            for (int envelope = 0; envelope < count; envelope++)
            {
                bool time = ReadDirection(ref bits, envelope, mode, previousCoherenceMode);
                AacPsCodebook book = time switch
                {
                    true => AacPsCodebook.CoherenceTime,
                    false => AacPsCodebook.CoherenceFrequency
                };
                ReadParameters(ref bits, Envelopes[envelope].Coherence,
                    envelope == 0 ? previous.Coherence : Envelopes[envelope - 1].Coherence, bands, time, book, 0, 7, false);
            }
        }

        private bool ReadDirection(ref AacBitReader bits, int envelope, int mode, int? previousMode)
        {
            bool time = bits.Read(1) != 0;
            if (envelope == 0 && time)
            {
                independent = false;
                if (previousMode is not null && previousMode != mode)
                {
                    throw new InvalidDataException("A changed PS parameter mode requires frequency differential coding.");
                }
            }
            return time;
        }

        private void ReadExtensions(ref AacBitReader bits, int count)
        {
            int bytes = bits.Read(4);
            if (bytes == 15)
            {
                bytes += bits.Read(8);
            }
            AacBitReader extension = bits.ReadSubstream(bytes * 8);
            bool phasePresent = false;
            while (extension.Remaining > 7)
            {
                int id = extension.Read(2);
                if (id == 0)
                {
                    if (phasePresent)
                    {
                        throw new InvalidDataException("The PS frame repeats its phase extension.");
                    }
                    phasePresent = true;
                    PhaseEnabled = extension.Read(1) != 0;
                    if (PhaseEnabled)
                    {
                        ReadPhase(ref extension, count);
                    }
                    if (extension.Read(1) != 0)
                    {
                        throw new InvalidDataException("The PS reserved bit is not zero.");
                    }
                }
                else
                {
                    extension.Skip(extension.Remaining);
                }
            }
            extension.Skip(extension.Remaining);
        }

        private void ReadPhase(ref AacBitReader bits, int count)
        {
            int mode = Header?.IntensityMode ?? throw new InvalidDataException("The PS phase extension requires an intensity mode.");
            int bands = AacPsHeader.PhaseCount(mode);
            for (int envelope = 0; envelope < count; envelope++)
            {
                bool time = ReadDirection(ref bits, envelope, mode, previousIntensityMode);
                ReadParameters(ref bits, Envelopes[envelope].PhaseDifference,
                    envelope == 0 ? previous.PhaseDifference : Envelopes[envelope - 1].PhaseDifference, bands, time,
                    time switch
                    {
                        true => AacPsCodebook.PhaseDifferenceTime,
                        false => AacPsCodebook.PhaseDifferenceFrequency
                    }, 0, 7, true);
                time = ReadDirection(ref bits, envelope, mode, previousIntensityMode);
                ReadParameters(ref bits, Envelopes[envelope].OverallPhase,
                    envelope == 0 ? previous.OverallPhase : Envelopes[envelope - 1].OverallPhase, bands, time,
                    time switch
                    {
                        true => AacPsCodebook.OverallPhaseTime,
                        false => AacPsCodebook.OverallPhaseFrequency
                    }, 0, 7, true);
            }
        }

        private static void ReadParameters(ref AacBitReader bits, Span<int> output, ReadOnlySpan<int> history,
            int count, bool time, AacPsCodebook book, int minimum, int maximum, bool modulo)
        {
            int prior = 0;
            for (int band = 0; band < count; band++)
            {
                int value = AacPsHuffman.Read(ref bits, book) + (time ? history[band] : prior);
                if (modulo)
                {
                    value &= 7;
                }
                if (value < minimum || value > maximum)
                {
                    throw new InvalidDataException("A PS parameter is outside its quantization range.");
                }
                output[band] = prior = value;
            }
        }
    }
}
