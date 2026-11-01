// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal static class AacAccessUnitParser
    {
        public static void Read(ReadOnlySpan<byte> data, int frequencyIndex, AacDecoderWorkspace workspace,
            ref uint noiseState, CancellationToken cancellationToken)
        {
            AacBitReader bits = new(data);
            int element = 0;
            int channel = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int type = bits.Read(3);
                if (type == 7)
                {
                    break;
                }
                switch (type)
                {
                    case 0:
                    case 3:
                        workspace.BindElement(element++, type, bits.Read(4));
                        AacChannelState single = workspace.Channels[channel++];
                        AacChannelParser.Read(ref bits, single, frequencyIndex, null, false, cancellationToken);
                        AacSpectralTools.Reconstruct(single, null, false, 0, [], ref noiseState, cancellationToken);
                        break;
                    case 1:
                        workspace.BindElement(element++, type, bits.Read(4));
                        ReadPair(ref bits, workspace.Channels[channel], workspace.Channels[channel + 1], frequencyIndex,
                            ref noiseState, cancellationToken);
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
                        break;
                    case 6:
                        ReadFill(ref bits);
                        break;
                    case 2:
                    case 5:
                        throw new NotSupportedException("AAC coupling and in-band program configurations are not supported.");
                    default:
                        throw new InvalidDataException("The AAC element identifier is invalid.");
                }
            }
            bits.Align();
            if (element != workspace.Layout.Elements.Length || bits.Remaining != 0)
            {
                throw new InvalidDataException("An AAC packet must contain one complete raw data block for the configured channels.");
            }
        }

        private static void ReadPair(ref AacBitReader bits, AacChannelState left, AacChannelState right, int frequencyIndex,
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
            AacChannelParser.Read(ref bits, left, frequencyIndex, info, false, cancellationToken);
            AacChannelParser.Read(ref bits, right, frequencyIndex, info, commonWindow, cancellationToken);
            AacSpectralTools.Reconstruct(left, right, commonWindow, maskMode, mask, ref noiseState, cancellationToken);
        }

        private static void ReadFill(ref AacBitReader bits)
        {
            int count = bits.Read(4);
            if (count == 15)
            {
                count += bits.Read(8) - 1;
            }
            if (count == 0)
            {
                return;
            }
            int extension = bits.Read(4);
            switch (extension)
            {
                case 0:
                    bits.Skip(count * 8 - 4);
                    break;
                case 1:
                    if (bits.Read(4) != 0)
                    {
                        throw new InvalidDataException("The AAC fill nibble must be zero.");
                    }
                    for (int index = 1; index < count; index++)
                    {
                        if (bits.Read(8) != 0xa5)
                        {
                            throw new InvalidDataException("The AAC fill byte is invalid.");
                        }
                    }
                    break;
                default:
                    throw new NotSupportedException("This AAC fill extension is not supported; SBR, spatial audio and dynamic range extensions require separate handling.");
            }
        }
    }
}
