// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal static class AacAccessUnitParser
    {
        public static void Read(ReadOnlySpan<byte> data, int frequencyIndex, int sbrSampleRate, AacDecoderWorkspace workspace,
            ref uint noiseState, CancellationToken cancellationToken, ReadOnlySpan<byte> protection = default)
        {
            workspace.BeginSbrFrame();
            ReadCore(data, frequencyIndex, sbrSampleRate, workspace.Layout, workspace.ElementTags, workspace,
                ref noiseState, cancellationToken, protection);
        }

        public static AacAccessUnitInspection Inspect(ReadOnlySpan<byte> data, int frequencyIndex, int sbrSampleRate,
            AacChannelLayout layout, CancellationToken cancellationToken, ReadOnlySpan<byte> protection = default)
        {
            Span<int> tags = stackalloc int[layout.Elements.Length];
            tags.Fill(-1);
            uint noiseState = 0;
            return ReadCore(data, frequencyIndex, sbrSampleRate, layout, tags, null, ref noiseState, cancellationToken, protection);
        }

        private static AacAccessUnitInspection ReadCore(ReadOnlySpan<byte> data, int frequencyIndex, int sbrSampleRate,
            AacChannelLayout layout, Span<int> tags, AacDecoderWorkspace? workspace, ref uint noiseState,
            CancellationToken cancellationToken, ReadOnlySpan<byte> protection)
        {
            AacBitReader bits = new(data);
            AacAdtsCrc crc = new(data, protection, frequencyIndex, layout.ChannelCount);
            int element = 0;
            int channel = 0;
            int previousType = -1;
            bool independent = true;
            bool hasSbr = false;
            bool hasParametricStereo = false;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int type = bits.Read(3);
                if (type == 7)
                {
                    break;
                }
                int start = bits.Position;
                switch (type)
                {
                    case 0:
                    case 3:
                        BindElement(layout.Elements, tags, element++, type, bits.Read(4));
                        if (workspace is null)
                        {
                            AacChannelParser.Inspect(ref bits, frequencyIndex, null, false, cancellationToken);
                        }
                        else
                        {
                            AacChannelState single = workspace.Channels[channel];
                            AacChannelParser.Read(ref bits, single, frequencyIndex, null, false, cancellationToken);
                            AacSpectralTools.Reconstruct(single, null, false, 0, [], ref noiseState, cancellationToken);
                        }
                        crc.AddChannel(start, bits.Position);
                        channel++;
                        break;
                    case 1:
                        BindElement(layout.Elements, tags, element++, type, bits.Read(4));
                        int secondStart = ReadPair(ref bits, workspace, channel, frequencyIndex, ref noiseState, cancellationToken);
                        crc.AddChannel(start, bits.Position);
                        crc.AddSecondChannel(secondStart, bits.Position);
                        channel += 2;
                        break;
                    case 4:
                        bits.Read(4);
                        bool aligned = bits.Read(1) != 0;
                        int count = bits.Read(8);
                        if (count == 255)
                        {
                            count += bits.Read(8);
                        }
                        if (aligned)
                        {
                            bits.Align();
                        }
                        bits.Skip(count * 8);
                        crc.AddDataElement(start, bits.Position);
                        break;
                    case 6:
                        independent &= ReadFill(ref bits, workspace, element - 1, previousType, sbrSampleRate, out bool sbr, out bool ps);
                        hasSbr |= sbr;
                        hasParametricStereo |= ps;
                        break;
                    case 2:
                    case 5:
                        throw new NotSupportedException("AAC coupling and in-band program configurations are not supported.");
                    default:
                        throw new InvalidDataException("The AAC element identifier is invalid.");
                }
                previousType = type;
            }
            bits.Align();
            if (element != layout.Elements.Length || bits.Remaining != 0)
            {
                throw new InvalidDataException("An AAC packet must contain one complete raw data block for the configured channels.");
            }
            crc.Verify();
            return new(independent, hasSbr, hasParametricStereo);
        }

        private static int ReadPair(ref AacBitReader bits, AacDecoderWorkspace? workspace, int channel, int frequencyIndex,
            ref uint noiseState, CancellationToken cancellationToken)
        {
            bool commonWindow = bits.Read(1) != 0;
            int maskMode = 0;
            Span<byte> mask = stackalloc byte[512];
            mask.Clear();
            AacWindowInfo? info = null;
            if (commonWindow)
            {
                info = AacWindowInfo.Read(ref bits, frequencyIndex);
                maskMode = bits.Read(2);
                if (maskMode == 3)
                {
                    throw new InvalidDataException("The AAC stereo mask mode is reserved.");
                }
                if (maskMode == 1)
                {
                    for (int group = 0; group < info.Groups.Length; group++)
                    {
                        for (int band = 0; band < info.MaxBands; band++)
                        {
                            mask[group * 64 + band] = (byte)bits.Read(1);
                        }
                    }
                }
            }
            int secondStart;
            if (workspace is null)
            {
                AacChannelParser.Inspect(ref bits, frequencyIndex, info, false, cancellationToken);
                secondStart = bits.Position;
                AacChannelParser.Inspect(ref bits, frequencyIndex, info, commonWindow, cancellationToken);
            }
            else
            {
                AacChannelState left = workspace.Channels[channel];
                AacChannelState right = workspace.Channels[channel + 1];
                AacChannelParser.Read(ref bits, left, frequencyIndex, info, false, cancellationToken);
                secondStart = bits.Position;
                AacChannelParser.Read(ref bits, right, frequencyIndex, info, commonWindow, cancellationToken);
                AacSpectralTools.Reconstruct(left, right, commonWindow, maskMode, mask, ref noiseState, cancellationToken);
            }
            return secondStart;
        }

        private static bool ReadFill(ref AacBitReader bits, AacDecoderWorkspace? workspace, int element, int previousType,
            int sbrSampleRate, out bool hasSbr, out bool hasParametricStereo)
        {
            hasSbr = false;
            hasParametricStereo = false;
            int count = bits.Read(4);
            if (count == 15)
            {
                count += bits.Read(8) - 1;
            }
            if (count == 0)
            {
                return true;
            }
            AacBitReader payload = bits.ReadSubstream(count * 8);
            int extension = payload.Read(4);
            switch (extension)
            {
                case 0:
                    payload.Skip(payload.Remaining);
                    break;
                case 1:
                    if (payload.Read(4) != 0)
                    {
                        throw new InvalidDataException("The AAC fill nibble must be zero.");
                    }
                    for (int index = 1; index < count; index++)
                    {
                        if (payload.Read(8) != 0xa5)
                        {
                            throw new InvalidDataException("The AAC fill byte is invalid.");
                        }
                    }
                    break;
                case 13:
                case 14:
                    if (element < 0 || previousType is not (0 or 1))
                    {
                        throw new InvalidDataException("An SBR payload must immediately follow its AAC channel element.");
                    }
                    hasSbr = true;
                    if (workspace is null)
                    {
                        return AacSbrElement.InspectRandomAccess(ref payload, extension == 14, previousType == 1 ? 2 : 1, sbrSampleRate, out hasParametricStereo);
                    }
                    AacSbrElement sbr = workspace.GetSbrElement(element, sbrSampleRate);
                    sbr.Read(ref payload, extension == 14);
                    hasParametricStereo = sbr.HasParametricStereoData;
                    break;
                default:
                    throw new NotSupportedException("This AAC fill extension is not supported; SBR, spatial audio and dynamic range extensions require separate handling.");
            }
            return true;
        }

        private static void BindElement(ReadOnlySpan<byte> layout, Span<int> tags, int index, int type, int tag)
        {
            if (index >= layout.Length || type != layout[index])
            {
                throw new InvalidDataException("The AAC channel elements do not match the configured speaker layout.");
            }
            if (tags[index] >= 0 && tags[index] != tag)
            {
                throw new InvalidDataException("The AAC element tag changed within the configured stream.");
            }
            for (int previous = 0; previous < index; previous++)
            {
                if (layout[previous] == type && tags[previous] == tag)
                {
                    throw new InvalidDataException("AAC elements of the same type must have unique instance tags.");
                }
            }
            tags[index] = tag;
        }
    }
}
