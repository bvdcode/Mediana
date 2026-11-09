// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Ac3
{
    internal partial class Ac3FrameDecoder
    {
        private readonly bool[] enhancedCouplingStrategy = new bool[6], enhancedCoupling = new bool[6];
        private readonly int[] enhancedExponents = new int[42];
        private bool blockSwitchExists, ditherExists, allocationMode, frameFastGain, deltaExists, skipExists, firstCouplingLeak;
        private int snrStrategy, frameFine;
        private static ReadOnlySpan<byte> DefaultCouplingMerge => [0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 1, 0, 1, 1, 1, 1, 1];
        private static ReadOnlySpan<byte> FrameExponentStrategies =>
            [
                1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 3, 1, 0, 0, 0, 2, 0, 1, 0, 0, 0, 3, 3,
                2, 0, 0, 2, 0, 0, 2, 0, 0, 2, 0, 3, 2, 0, 0, 3, 2, 0, 2, 0, 0, 3, 3, 3,
                2, 0, 1, 0, 0, 0, 2, 0, 2, 0, 0, 3, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 3, 3,
                2, 0, 3, 2, 0, 0, 2, 0, 3, 2, 0, 3, 2, 0, 3, 3, 2, 0, 2, 0, 3, 3, 3, 3,
                3, 1, 0, 0, 0, 0, 3, 1, 0, 0, 0, 3, 3, 2, 0, 0, 2, 0, 3, 2, 0, 0, 3, 3,
                3, 2, 0, 2, 0, 0, 3, 2, 0, 2, 0, 3, 3, 2, 0, 3, 2, 0, 3, 2, 0, 3, 3, 3,
                3, 3, 1, 0, 0, 0, 3, 3, 2, 0, 0, 3, 3, 3, 2, 0, 2, 0, 3, 3, 2, 0, 3, 3,
                3, 3, 3, 2, 0, 0, 3, 3, 3, 2, 0, 3, 3, 3, 3, 3, 2, 0, 3, 3, 3, 3, 3, 3
            ];

        private void ReadEnhancedHeader(ref Ac3BitReader bits)
        {
            bits.Skip(45);
            for (int ch = 0; ch < (header.ChannelMode == 0 ? 2 : 1); ch++)
            {
                bits.Skip(5); if (bits.Flag()) { bits.Skip(8); }
            }
            if (header.StreamType == 1 && bits.Flag())
            {
                ushort map = (ushort)bits.Read(16);
                if (Mediana.Formats.Audio.Eac3ChannelMap.GetChannelCount(map) != header.ChannelCount)
                { throw new InvalidDataException("The dependent channel map disagrees with its coded channel count."); }
            }
            if (bits.Flag()) { SkipMixMetadata(ref bits); }
            if (bits.Flag())
            {
                bits.Skip(5);
                if (header.ChannelMode == 2) { bits.Skip(4); }
                if (header.ChannelMode >= 6) { bits.Skip(2); }
                for (int ch = 0; ch < (header.ChannelMode == 0 ? 2 : 1); ch++) { if (bits.Flag()) { bits.Skip(8); } }
                if (header.SampleRate >= 32000) { bits.Skip(1); }
            }
            int blocks = header.SamplesPerFrame / 256;
            if (header.StreamType == 0 && blocks != 6) { bits.Skip(1); }
            if (header.StreamType == 2 && (blocks == 6 || bits.Flag())) { bits.Skip(6); }
            if (bits.Flag()) { bits.Skip((bits.Read(6) + 1) * 8); }
            ReadEnhancedFrame(ref bits, blocks);
        }

        private void SkipMixMetadata(ref Ac3BitReader bits)
        {
            int mode = header.ChannelMode;
            if (mode > 2) { bits.Skip(2); }
            if ((mode & 1) != 0 && mode > 2) { bits.Skip(6); }
            if ((mode & 4) != 0) { bits.Skip(6); }
            if (header.HasLfe && bits.Flag()) { bits.Skip(5); }
            if (header.StreamType != 0) { return; }
            if (bits.Flag()) { bits.Skip(6); }
            if (mode == 0 && bits.Flag()) { bits.Skip(6); }
            if (bits.Flag()) { bits.Skip(6); }
            switch (bits.Read(2))
            {
                case 0: break;
                case 1: bits.Skip(5); break;
                case 2: bits.Skip(12); break;
                case 3:
                    bits.Skip((bits.Read(5) + 2) * 8);
                    bits.Skip((8 - (bits.Position & 7)) & 7);
                    break;
            }
            if (mode < 2)
            {
                if (bits.Flag()) { bits.Skip(14); }
                if (mode == 0 && bits.Flag()) { bits.Skip(14); }
            }
            if (bits.Flag())
            {
                int blocks = header.SamplesPerFrame / 256;
                for (int block = 0; block < blocks; block++) { if (blocks == 1 || bits.Flag()) { bits.Skip(5); } }
            }
        }

        private void ReadEnhancedFrame(ref Ac3BitReader bits, int blocks)
        {
            bool explicitExponents = blocks != 6 || bits.Flag();
            bool ahtExists = blocks == 6 && bits.Flag();
            snrStrategy = bits.Read(2);
            if (snrStrategy == 3) { throw new InvalidDataException("The E-AC-3 SNR offset strategy is reserved."); }
            bool transientExists = bits.Flag();
            blockSwitchExists = bits.Flag(); ditherExists = bits.Flag(); allocationMode = bits.Flag();
            frameFastGain = bits.Flag(); deltaExists = bits.Flag(); skipExists = bits.Flag();
            bool attenuationExists = bits.Flag();
            bool inUse = false;
            for (int block = 0; block < blocks; block++)
            {
                enhancedCouplingStrategy[block] = header.ChannelMode > 1 && (block == 0 || bits.Flag());
                if (enhancedCouplingStrategy[block]) { inUse = bits.Flag(); }
                enhancedCoupling[block] = inUse;
            }
            Array.Clear(enhancedExponents);
            if (explicitExponents)
            {
                for (int block = 0; block < blocks; block++)
                {
                    if (enhancedCoupling[block]) { enhancedExponents[block * 7 + 6] = bits.Read(2); }
                    for (int ch = 0; ch < fullChannels; ch++) { enhancedExponents[block * 7 + ch] = bits.Read(2); }
                }
            }
            else
            {
                if (enhancedCoupling.AsSpan(0, blocks).Contains(true))
                {
                    int code = bits.Read(5);
                    for (int block = 0; block < blocks; block++)
                    {
                        if (enhancedCoupling[block]) { enhancedExponents[block * 7 + 6] = FrameExponentStrategies[code * 6 + block]; }
                    }
                }
                for (int ch = 0; ch < fullChannels; ch++)
                {
                    int code = bits.Read(5);
                    for (int block = 0; block < blocks; block++) { enhancedExponents[block * 7 + ch] = FrameExponentStrategies[code * 6 + block]; }
                }
            }
            if (header.HasLfe)
            {
                for (int block = 0; block < blocks; block++) { enhancedExponents[block * 7 + fullChannels] = bits.Read(1); }
            }
            if (header.StreamType == 0 && (blocks == 6 || bits.Flag())) { bits.Skip(5 * fullChannels); }
            if (ahtExists)
            {
                for (int item = 0; item <= header.ChannelCount; item++)
                {
                    int ch = item == 0 ? 6 : item - 1;
                    if (ch >= header.ChannelCount && ch != 6) { continue; }
                    int regions = 0, active = 0;
                    for (int block = 0; block < blocks; block++)
                    {
                        if (ch != 6 || enhancedCoupling[block])
                        {
                            active++; if (enhancedExponents[block * 7 + ch] != 0 || ch == 6 && enhancedCouplingStrategy[block]) { regions++; }
                        }
                    }
                    if (active == 6 && regions == 1 && bits.Flag()) { adaptiveTransform.Enable(ch); }
                }
            }
            if (snrStrategy == 0) { coarse = bits.Read(6); frameFine = bits.Read(4); }
            if (transientExists)
            {
                for (int ch = 0; ch < fullChannels; ch++)
                {
                    if (bits.Flag()) { transientLocations[ch] = bits.Read(10) * 4; transientLengths[ch] = bits.Read(8); }
                }
            }
            if (attenuationExists)
            {
                spectralExtension.ReadAttenuation(ref bits, fullChannels);
            }
            if (blocks != 1 && bits.Flag())
            {
                int words = header.FrameBytes / 2;
                int sizeBits = BitOperations.Log2((uint)(words - 1)) + 1;
                bits.Skip((blocks - 1) * (4 + sizeBits));
            }
            firstCouplingLeak = true;
        }

        private void ReadEnhancedBlock(ref Ac3BitReader bits, int block)
        {
            bool allocationChanged = block == 0;
            for (int ch = 0; ch < fullChannels; ch++) { shortBlock[ch] = blockSwitchExists && bits.Flag(); }
            for (int ch = 0; ch < fullChannels; ch++) { dither[ch] = !ditherExists || bits.Flag(); }
            ReadDynamics(ref bits);
            spectralExtension.ReadBlock(ref bits, block, fullChannels, header.ChannelMode == 1);
            if (enhancedCouplingStrategy[block])
            {
                ReadCoupling(ref bits, enhancedCoupling[block], true);
                if (!coupling) { firstCouplingLeak = true; }
            }
            ReadCoordinates(ref bits, true);
            ReadRematrix(ref bits, block, true);
            for (int ch = 0; ch < 7; ch++) { strategies[ch] = enhancedExponents[block * 7 + ch]; }
            ReadExponents(ref bits, block);
            if (allocationMode)
            {
                if (bits.Flag()) { ReadAllocationParameters(ref bits); allocationChanged = true; }
                else if (block == 0) { throw new InvalidDataException("Initial E-AC-3 bit allocation parameters are missing."); }
            }
            else { slowDecay = 2; fastDecay = 1; slowGain = 1; dbKnee = 2; floor = 7; }
            if (snrStrategy == 0)
            {
                fine.AsSpan().Fill(frameFine);
            }
            else if (block == 0 || bits.Flag())
            {
                allocationChanged = true;
                coarse = bits.Read(6);
                if (snrStrategy == 1) { fine.AsSpan().Fill(bits.Read(4)); }
                else
                {
                    if (coupling) { fine[6] = bits.Read(4); }
                    for (int ch = 0; ch < header.ChannelCount; ch++) { fine[ch] = bits.Read(4); }
                }
            }
            if (frameFastGain && bits.Flag())
            {
                allocationChanged = true;
                if (coupling) { fastGain[6] = bits.Read(3); }
                for (int ch = 0; ch < header.ChannelCount; ch++) { fastGain[ch] = bits.Read(3); }
            }
            else
            {
                for (int ch = 0; ch < fastGain.Length; ch++) { allocationChanged |= fastGain[ch] != 4; fastGain[ch] = 4; }
            }
            if (header.StreamType == 0 && bits.Flag()) { bits.Skip(10); }
            if (coupling && (firstCouplingLeak || bits.Flag()))
            {
                fastLeak = bits.Read(3); slowLeak = bits.Read(3); firstCouplingLeak = false; allocationChanged = true;
            }
            bool deltaChanged = deltaExists && bits.Flag();
            ReadDelta(ref bits, deltaChanged);
            if (skipExists && bits.Flag()) { bits.Skip(bits.Read(9) * 8); }
            ComputeAllocation(allocationChanged || deltaChanged);
        }
    }
}
