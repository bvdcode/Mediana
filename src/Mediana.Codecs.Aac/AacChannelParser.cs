namespace Mediana.Codecs.Aac
{
    internal static class AacChannelParser
    {
        public static void Read(ref AacBitReader bits, AacChannelState channel, int frequencyIndex,
            AacWindowInfo? commonWindow, bool allowIntensity, CancellationToken cancellationToken)
        {
            channel.ClearFrame();
            int gain = bits.Read(8);
            channel.Info = commonWindow ?? AacWindowInfo.Read(ref bits, frequencyIndex);
            ReadSections(ref bits, channel, allowIntensity);
            ReadScales(ref bits, channel, gain);
            Span<int> pulsePositions = stackalloc int[4];
            Span<int> pulseAmplitudes = stackalloc int[4];
            int pulses = 0;
            if (bits.Read(1) != 0)
            {
                pulses = ReadPulses(ref bits, channel.Info, pulsePositions, pulseAmplitudes);
            }
            if (bits.Read(1) != 0)
            {
                AacTemporalNoiseShaping.Read(ref bits, channel);
            }
            if (bits.Read(1) != 0)
            {
                throw new InvalidDataException("Gain control data is not permitted in AAC-LC.");
            }
            ReadSpectrum(ref bits, channel, cancellationToken);
            for (int index = 0; index < pulses; index++)
            {
                ref int value = ref channel.Quantized[pulsePositions[index]];
                value += value > 0 ? pulseAmplitudes[index] : -pulseAmplitudes[index];
                if (Math.Abs(value) > 8191)
                {
                    throw new InvalidDataException("The AAC pulse exceeds the spectral magnitude limit.");
                }
            }
        }

        private static void ReadSections(ref AacBitReader bits, AacChannelState channel, bool allowIntensity)
        {
            AacWindowInfo info = channel.Info;
            int width = info.ShortWindow ? 3 : 5;
            int escape = (1 << width) - 1;
            for (int group = 0; group < info.Groups.Length; group++)
            {
                int band = 0;
                while (band < info.MaxBands)
                {
                    int codebook = bits.Read(4);
                    if (codebook == 12 || (!allowIntensity && codebook is 14 or 15))
                    {
                        throw new InvalidDataException("The AAC section uses a codebook that is not permitted in this channel.");
                    }
                    int length = 0;
                    int increment;
                    do
                    {
                        increment = bits.Read(width);
                        length += increment;
                        if (length > info.MaxBands - band)
                        {
                            throw new InvalidDataException("The AAC section exceeds the active band count.");
                        }
                    } while (increment == escape);
                    if (length == 0)
                    {
                        throw new InvalidDataException("The AAC section length must be positive.");
                    }
                    channel.Books.Slice(group * 64 + band, length).Fill((byte)codebook);
                    band += length;
                }
            }
        }

        private static void ReadScales(ref AacBitReader bits, AacChannelState channel, int gain)
        {
            int scale = gain;
            int intensity = 0;
            int noise = gain - 90 - 256;
            bool firstNoise = true;
            for (int group = 0; group < channel.Info.Groups.Length; group++)
            {
                for (int band = 0; band < channel.Info.MaxBands; band++)
                {
                    int index = group * 64 + band;
                    int value;
                    switch (channel.Books[index])
                    {
                        case 0:
                            continue;
                        case 13:
                            noise += firstNoise ? bits.Read(9) : AacHuffman.ReadScaleFactor(ref bits);
                            firstNoise = false;
                            value = noise;
                            break;
                        case 14:
                        case 15:
                            intensity += AacHuffman.ReadScaleFactor(ref bits);
                            value = intensity;
                            break;
                        case >= 1 and <= 11:
                            scale += AacHuffman.ReadScaleFactor(ref bits);
                            if (scale is < 0 or > 255)
                            {
                                throw new InvalidDataException("The AAC scale factor is outside its permitted range.");
                            }
                            value = scale;
                            break;
                        default:
                            throw new InvalidDataException("The AAC scale factor codebook is invalid.");
                    }
                    channel.Scales[index] = value;
                }
            }
        }

        private static void ReadSpectrum(ref AacBitReader bits, AacChannelState channel, CancellationToken cancellationToken)
        {
            AacWindowInfo info = channel.Info;
            Span<int> quantized = channel.Quantized;
            ReadOnlySpan<byte> books = channel.Books;
            Span<int> tuple = stackalloc int[4];
            int firstWindow = 0;
            for (int group = 0; group < info.Groups.Length; group++)
            {
                for (int band = 0; band < info.MaxBands; band++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int codebook = books[group * 64 + band];
                    if (codebook is 0 or 13 or 14 or 15)
                    {
                        continue;
                    }
                    for (int window = 0; window < info.Groups[group]; window++)
                    {
                        int start = (firstWindow + window) * info.WindowSize + info.Bands[band];
                        int end = (firstWindow + window) * info.WindowSize + info.Bands[band + 1];
                        while (start < end)
                        {
                            int count = AacHuffman.ReadSpectrum(ref bits, codebook, tuple);
                            if (count > end - start)
                            {
                                throw new InvalidDataException("The AAC spectral tuple crosses a band boundary.");
                            }
                            tuple[..count].CopyTo(quantized[start..]);
                            start += count;
                        }
                    }
                }
                firstWindow += info.Groups[group];
            }
        }

        private static int ReadPulses(ref AacBitReader bits, AacWindowInfo info,
            scoped Span<int> positions, scoped Span<int> amplitudes)
        {
            if (info.ShortWindow)
            {
                throw new InvalidDataException("AAC pulse data is not permitted in short windows.");
            }
            int count = bits.Read(2) + 1;
            int band = bits.Read(6);
            if (band >= info.Bands.Length - 1)
            {
                throw new InvalidDataException("The AAC pulse start band is invalid.");
            }
            int position = info.Bands[band];
            for (int index = 0; index < count; index++)
            {
                position += bits.Read(5);
                if (position >= 1024)
                {
                    throw new InvalidDataException("The AAC pulse exceeds the spectral buffer.");
                }
                positions[index] = position;
                amplitudes[index] = bits.Read(4);
            }
            return count;
        }
    }
}
