namespace Mediana.Codecs.Aac
{
    internal static class AacAccessUnitParser
    {
        public static void Read(ReadOnlySpan<byte> data, int frequencyIndex, AacDecoderWorkspace workspace,
            ref uint noiseState, CancellationToken cancellationToken)
        {
            AacBitReader bits = new(data);
            bool foundAudio = false;
            bool commonWindow = false;
            int maskMode = 0;
            Span<byte> mask = stackalloc byte[512];
            mask.Clear();
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
                        RequireSingleElement(foundAudio, workspace.Right is null);
                        bits.Read(4);
                        AacChannelParser.Read(ref bits, workspace.Left, frequencyIndex, null, false, cancellationToken);
                        foundAudio = true;
                        break;
                    case 1:
                        RequireSingleElement(foundAudio, workspace.Right is not null);
                        bits.Read(4);
                        commonWindow = bits.Read(1) != 0;
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
                        AacChannelParser.Read(ref bits, workspace.Left, frequencyIndex, info, false, cancellationToken);
                        AacChannelParser.Read(ref bits, workspace.Right!, frequencyIndex, info, commonWindow, cancellationToken);
                        foundAudio = true;
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
                    case 3:
                    case 5:
                        throw new NotSupportedException("AAC coupling, LFE and in-band program configurations are not supported.");
                    default:
                        throw new InvalidDataException("The AAC element identifier is invalid.");
                }
            }
            bits.Align();
            if (!foundAudio || bits.Remaining != 0)
            {
                throw new InvalidDataException("An AAC packet must contain one complete raw data block for the configured channels.");
            }
            AacSpectralTools.Reconstruct(workspace.Left, workspace.Right, commonWindow, maskMode, mask, ref noiseState, cancellationToken);
        }

        private static void RequireSingleElement(bool foundAudio, bool matchingLayout)
        {
            if (foundAudio || !matchingLayout)
            {
                throw new InvalidDataException("The AAC channel elements do not match the configured mono or stereo layout.");
            }
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
