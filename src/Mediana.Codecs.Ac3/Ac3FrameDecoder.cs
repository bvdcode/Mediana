// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3
{
    internal partial class Ac3FrameDecoder(MediaBufferAllocator allocator, bool applyDynamicRangeCompression = true) : IDisposable
    {
        private readonly Ac3Workspace workspace = new(allocator);
        private readonly Ac3SpectralExtension spectralExtension = new(allocator);
        private readonly Ac3AdaptiveTransform adaptiveTransform = new(allocator);
        private readonly Ac3EnhancedCoupling enhancedCoordinates = new(allocator);
        private static readonly ushort[] CrcTable = CreateCrcTable();
        private readonly int[] end = new int[7], strategies = new int[7], fine = new int[7], fastGain = new int[7], deltaMode = new int[7];
        private readonly bool[] coupled = new bool[5], shortBlock = new bool[5], dither = new bool[5], coordinateNew = new bool[5], coordinateValid = new bool[5], rematrix = new bool[4];
        private readonly bool[] couplingMerge = new bool[18];
        private readonly double[] dynamics = new double[2], frameDynamics = new double[12];
        internal ReadOnlySpan<double> FrameDynamics => frameDynamics;
        internal bool DynamicRangeMetadataSeen { get; private set; }
        private readonly int[] transientLocations = new int[5], transientLengths = new int[5];
        internal ReadOnlySpan<int> TransientLocations => transientLocations;
        internal ReadOnlySpan<int> TransientLengths => transientLengths;
        private Ac3FrameHeader header = null!;
        private int fullChannels, coarse, slowDecay, fastDecay, slowGain, dbKnee, floor, couplingStart, couplingEnd, subbands, fastLeak, slowLeak;
        private bool coupling, phaseInUse, couplingExponentsRequired, enhancedCouplingInUse;
        private uint random = 1;

        public static void ValidateFrame(ReadOnlySpan<byte> data, Ac3FrameHeader header)
        {
            if (data.Length != header.FrameBytes) { throw new InvalidDataException("An AC-3 packet must contain exactly one complete syncframe."); }
            int crc = 0;
            foreach (byte value in data[2..])
            {
                crc = (crc << 8 ^ CrcTable[(crc >> 8) ^ value]) & 0xffff;
            }
            if (crc != 0) { throw new InvalidDataException("The AC-3 syncframe checksum is invalid."); }
        }

        private static ushort[] CreateCrcTable()
        {
            ushort[] result = new ushort[256];
            for (int index = 0; index < result.Length; index++)
            {
                int value = index << 8;
                for (int bit = 0; bit < 8; bit++) { value = value << 1 ^ ((value & 0x8000) != 0 ? 0x8005 : 0); }
                result[index] = (ushort)value;
            }
            return result;
        }

        public void Decode(ReadOnlySpan<byte> data, Ac3FrameHeader frameHeader, Span<float> output, CancellationToken cancellationToken)
        {
            DecodeMapped(data, frameHeader, output, frameHeader.ChannelCount, default, default, cancellationToken);
        }

        internal void DecodeMapped(ReadOnlySpan<byte> data, Ac3FrameHeader frameHeader, Span<float> output,
            int outputChannels, ReadOnlySpan<int> channelMap, ReadOnlySpan<double> programDynamics, CancellationToken cancellationToken)
        {
            header = frameHeader;
            DynamicRangeMetadataSeen = false;
            fullChannels = header.ChannelCount - (header.HasLfe ? 1 : 0);
            Array.Clear(coupled); Array.Clear(coordinateValid); Array.Clear(rematrix);
            Array.Clear(end); Array.Clear(fine); Array.Clear(fastGain); Array.Clear(deltaMode);
            transientLocations.AsSpan().Fill(-1);
            coupling = false; phaseInUse = false; enhancedCouplingInUse = false; dynamics.AsSpan().Fill(1);
            spectralExtension.ResetFrame(); adaptiveTransform.ResetFrame(); enhancedCoordinates.ResetFrame();
            for (int ch = 0; ch < 7; ch++) { workspace.Delta(ch).Clear(); }
            Ac3BitReader bits = new(data);
            if (header.IsEnhanced) { ReadEnhancedHeader(ref bits); }
            else { ReadHeader(ref bits); }
            for (int block = 0; block < header.SamplesPerFrame / 256; block++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (header.IsEnhanced) { ReadEnhancedBlock(ref bits, block); }
                else { ReadBlock(ref bits, block); }
                frameDynamics[block * 2] = dynamics[0]; frameDynamics[block * 2 + 1] = dynamics[header.ChannelMode == 0 ? 1 : 0];
                ReadMantissas(ref bits, block);
                ApplyRematrix();
                for (int ch = 0; ch < fullChannels; ch++) { spectralExtension.Process(ch, workspace.Spectrum(ch), ref random); }
                for (int ch = 0; ch < header.ChannelCount; ch++)
                {
                    int destination = channelMap.IsEmpty ? ch : channelMap[ch];
                    if (destination < 0) { continue; }
                    int component = header.ChannelMode == 0 && ch == 1 ? 1 : 0;
                    double gain = 1;
                    if (applyDynamicRangeCompression)
                    { gain = programDynamics.IsEmpty ? dynamics[component] : programDynamics[block * 2 + component]; }
                    Ac3Synthesis.Process(workspace.Spectrum(ch), workspace.Overlap(ch), output[(block * 256 * outputChannels + destination)..],
                        outputChannels, ch < fullChannels && shortBlock[ch], gain);
                }
                if (bits.Remaining < 18) { throw new InvalidDataException("AC-3 audio data overlaps its checksum."); }
            }
        }

        private void ReadHeader(ref Ac3BitReader bits)
        {
            bits.Skip(48);
            int mode = bits.Read(3);
            if ((mode & 1) != 0 && mode != 1) { bits.Skip(2); }
            if ((mode & 4) != 0) { bits.Skip(2); }
            if (mode == 2) { bits.Skip(2); }
            bits.Skip(1);
            for (int ch = 0; ch < (mode == 0 ? 2 : 1); ch++)
            {
                bits.Skip(5);
                if (bits.Flag()) { bits.Skip(8); }
                if (bits.Flag()) { bits.Skip(8); }
                if (bits.Flag()) { bits.Skip(7); }
            }
            bits.Skip(2);
            if (bits.Flag()) { bits.Skip(14); }
            if (bits.Flag()) { bits.Skip(14); }
            if (bits.Flag()) { bits.Skip((bits.Read(6) + 1) * 8); }
        }

        private void ReadBlock(ref Ac3BitReader bits, int block)
        {
            for (int ch = 0; ch < fullChannels; ch++) { shortBlock[ch] = bits.Flag(); }
            for (int ch = 0; ch < fullChannels; ch++) { dither[ch] = bits.Flag(); }
            ReadDynamics(ref bits);
            bool strategyNew = bits.Flag();
            if (!strategyNew && block == 0) { throw new InvalidDataException("The first AC-3 block must define its coupling strategy."); }
            if (strategyNew) { ReadCoupling(ref bits, bits.Flag(), false); }
            ReadCoordinates(ref bits, false);
            ReadRematrix(ref bits, block, false);
            strategies[6] = coupling ? bits.Read(2) : 0;
            for (int ch = 0; ch < fullChannels; ch++) { strategies[ch] = bits.Read(2); }
            if (header.HasLfe) { strategies[fullChannels] = bits.Read(1); }
            ReadExponents(ref bits, block);
            bool allocation = bits.Flag();
            if (block == 0 && !allocation) { throw new InvalidDataException("The first AC-3 block must define bit allocation parameters."); }
            if (allocation) { ReadAllocationParameters(ref bits); }
            bool snr = bits.Flag();
            if (block == 0 && !snr) { throw new InvalidDataException("The first AC-3 block must define SNR offsets."); }
            if (snr)
            {
                coarse = bits.Read(6);
                if (coupling) { fine[6] = bits.Read(4); fastGain[6] = bits.Read(3); }
                for (int ch = 0; ch < header.ChannelCount; ch++) { fine[ch] = bits.Read(4); fastGain[ch] = bits.Read(3); }
            }
            bool leakChanged = false;
            if (coupling)
            {
                bool leak = bits.Flag();
                if (block == 0 && !leak) { throw new InvalidDataException("Initial AC-3 coupling leak values are missing."); }
                if (leak) { fastLeak = bits.Read(3); slowLeak = bits.Read(3); }
                leakChanged = leak;
            }
            bool deltaChanged = bits.Flag();
            ReadDelta(ref bits, deltaChanged);
            if (bits.Flag()) { bits.Skip(bits.Read(9) * 8); }
            ComputeAllocation(allocation || snr || deltaChanged || leakChanged);
        }

        private void ReadDynamics(ref Ac3BitReader bits)
        {
            for (int ch = 0; ch < (header.ChannelMode == 0 ? 2 : 1); ch++)
            {
                if (bits.Flag())
                {
                    DynamicRangeMetadataSeen = true;
                    int value = bits.Read(8), exponent = (value >> 5) - ((value & 128) != 0 ? 8 : 0);
                    dynamics[ch] = Math.ScaleB((value & 31) + 32, exponent - 5);
                }
            }
        }

        private void ReadExponents(ref Ac3BitReader bits, int block)
        {
            for (int ch = 0; ch < fullChannels; ch++)
            {
                if (strategies[ch] == 0)
                {
                    if ((coupled[ch] && end[ch] != couplingStart) ||
                        (spectralExtension.UsesChannel(ch) && !coupled[ch] && end[ch] != spectralExtension.BeginBin))
                    { throw new InvalidDataException("Changed AC-3 channel bandwidth requires new exponents."); }
                    continue;
                }
                if (coupled[ch]) { end[ch] = couplingStart; }
                else if (spectralExtension.UsesChannel(ch)) { end[ch] = spectralExtension.BeginBin; }
                else
                {
                    int bandwidth = bits.Read(6);
                    if (bandwidth > 60) { throw new InvalidDataException("The AC-3 bandwidth code is reserved."); }
                    end[ch] = 73 + bandwidth * 3;
                }
            }
            if (coupling)
            {
                if (strategies[6] == 0 && (block == 0 || couplingExponentsRequired)) { throw new InvalidDataException("Initial or changed AC-3 coupling requires new exponents."); }
                if (strategies[6] != 0) { ExpandExponents(ref bits, 6, couplingStart, couplingEnd, strategies[6]); couplingExponentsRequired = false; }
            }
            for (int ch = 0; ch < header.ChannelCount; ch++)
            {
                if (strategies[ch] == 0)
                {
                    if (block == 0) { throw new InvalidDataException("Initial AC-3 channel exponents are missing."); }
                    continue;
                }
                if (ch == fullChannels) { end[ch] = 7; }
                ExpandExponents(ref bits, ch, 0, end[ch], strategies[ch]);
                if (ch < fullChannels) { bits.Skip(2); }
            }
        }

        private void ExpandExponents(ref Ac3BitReader bits, int channel, int start, int finish, int strategy)
        {
            int repeat = 1 << (strategy - 1), group = 3 * repeat;
            int groups = channel == 6 ? (finish - start) / group : (finish - 1 + group - 3) / group;
            int exponent = bits.Read(4), bin = start;
            Span<byte> values = workspace.Exponents(channel);
            if (channel == 6) { exponent *= 2; } else { values[bin++] = (byte)exponent; }
            for (int g = 0; g < groups; g++)
            {
                int code = bits.Read(7);
                if (code >= 125) { throw new InvalidDataException("The AC-3 exponent group is reserved."); }
                for (int divisor = 25; divisor != 0; divisor /= 5)
                {
                    exponent += code / divisor - 2; code %= divisor;
                    if (exponent is < 0 or > 24) { throw new InvalidDataException("An AC-3 exponent is outside the valid range."); }
                    for (int n = 0; n < repeat; n++) { if (bin < finish) { values[bin] = (byte)exponent; } bin++; }
                }
            }
        }

        private void ReadAllocationParameters(ref Ac3BitReader bits)
        {
            slowDecay = bits.Read(2); fastDecay = bits.Read(2); slowGain = bits.Read(2); dbKnee = bits.Read(2); floor = bits.Read(3);
        }

        private void ReadDelta(ref Ac3BitReader bits, bool present)
        {
            if (!present) { return; }
            if (coupling) { deltaMode[6] = bits.Read(2); }
            for (int ch = 0; ch < fullChannels; ch++) { deltaMode[ch] = bits.Read(2); }
            if (coupling) { ReadDeltaChannel(ref bits, 6); }
            for (int ch = 0; ch < fullChannels; ch++) { ReadDeltaChannel(ref bits, ch); }
        }

        private void ReadDeltaChannel(ref Ac3BitReader bits, int channel)
        {
            int mode = deltaMode[channel];
            if (mode == 3) { throw new InvalidDataException("The AC-3 delta bit allocation strategy is reserved."); }
            if (mode == 0) { return; }
            Span<int> delta = workspace.Delta(channel); delta.Clear();
            if (mode == 2) { return; }
            int count = bits.Read(3) + 1, band = 0;
            for (int segment = 0; segment < count; segment++)
            {
                band += bits.Read(5);
                int length = bits.Read(4), value = bits.Read(3);
                if (band + length > delta.Length) { throw new InvalidDataException("AC-3 delta bit allocation exceeds the frequency bands."); }
                delta.Slice(band, length).Fill((value < 4 ? value - 4 : value - 3) * 128); band += length;
            }
        }

        private void ComputeAllocation(bool parametersChanged)
        {
            bool zero = coarse == 0;
            for (int ch = 0; ch < header.ChannelCount; ch++) { zero &= fine[ch] == 0; }
            if (coupling) { zero &= fine[6] == 0; }
            for (int ch = 0; ch < 7; ch++)
            {
                if (ch >= header.ChannelCount && (ch != 6 || !coupling)) { continue; }
                if (!parametersChanged && strategies[ch] == 0) { continue; }
                int start = ch == 6 ? couplingStart : 0, finish = ch == 6 ? couplingEnd : end[ch];
                if (zero) { workspace.Bap(ch).Clear(); continue; }
                Ac3BitAllocation.Compute(workspace.Exponents(ch), workspace.Bap(ch), start, finish, header.SampleRateCode,
                    slowDecay, fastDecay, slowGain, dbKnee, floor, (((coarse - 15) << 4) + fine[ch]) << 2,
                    fastGain[ch], fastLeak, slowLeak, workspace.Delta(ch), adaptiveTransform.IsEnabled(ch));
            }
        }

        public void Reset() { workspace.Reset(); random = 1; }
        public void Dispose() { workspace.Dispose(); spectralExtension.Dispose(); adaptiveTransform.Dispose(); enhancedCoordinates.Dispose(); }
    }
}
