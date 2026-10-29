using Mediana.Core.Audio;

namespace Mediana.Codecs.Flac
{
    internal readonly record struct FlacFrameHeader(int BlockSize, int ChannelAssignment, bool VariableBlockSize,
        long Number, int ByteLength)
    {
        public static bool TryRead(ReadOnlySpan<byte> data, FlacConfiguration configuration, out FlacFrameHeader header)
        {
            header = default;
            if (data.Length < 5)
            {
                return false;
            }
            if (data[0] != 0xff || (data[1] & 0xfe) != 0xf8 || (data[3] & 1) != 0)
            {
                throw new InvalidDataException("The FLAC frame sync or reserved header bit is invalid.");
            }
            bool variable = (data[1] & 1) != 0;
            int blockCode = data[2] >> 4;
            int rateCode = data[2] & 15;
            int assignment = data[3] >> 4;
            int precision = ((data[3] >> 1) & 7) switch
            {
                0 => configuration.BitsPerSample,
                1 => 8,
                2 => 12,
                3 => throw new InvalidDataException("The FLAC frame bit depth code is reserved."),
                4 => 16,
                5 => 20,
                6 => 24,
                7 => 32,
                _ => throw new InvalidOperationException()
            };
            int channels = assignment switch
            {
                >= 0 and <= 7 => assignment + 1,
                >= 8 and <= 10 => 2,
                _ => throw new InvalidDataException("The FLAC channel assignment is reserved.")
            };
            if (channels != configuration.ChannelCount || precision != configuration.BitsPerSample)
            {
                throw new NotSupportedException("Changing FLAC channel count or precision within a stream is not supported.");
            }
            FlacBitReader reader = new(data, 32);
            if (!TryReadNumber(ref reader, variable, out long number))
            {
                return false;
            }
            int blockSize;
            switch (blockCode)
            {
                case 0:
                    throw new InvalidDataException("The FLAC block size code is reserved.");
                case 1:
                    blockSize = 192;
                    break;
                case >= 2 and <= 5:
                    blockSize = 144 << blockCode;
                    break;
                case 6:
                case 7:
                    if (!reader.TryRead(blockCode switch { 6 => 8, 7 => 16, _ => throw new InvalidOperationException() }, out ulong uncommonBlock))
                    {
                        return false;
                    }
                    blockSize = (int)uncommonBlock + 1;
                    break;
                case >= 8 and <= 15:
                    blockSize = 1 << blockCode;
                    break;
                default:
                    throw new InvalidOperationException();
            }
            int sampleRate;
            switch (rateCode)
            {
                case 12:
                case 13:
                case 14:
                    if (!reader.TryRead(rateCode switch { 12 => 8, 13 or 14 => 16, _ => throw new InvalidOperationException() }, out ulong uncommonRate))
                    {
                        return false;
                    }
                    sampleRate = (int)uncommonRate * (rateCode switch { 12 => 1000, 13 => 1, 14 => 10, _ => throw new InvalidOperationException() });
                    break;
                default:
                    sampleRate = rateCode switch
                    {
                        0 => configuration.SampleRate,
                        1 => 88200,
                        2 => 176400,
                        3 => 192000,
                        4 => 8000,
                        5 => 16000,
                        6 => 22050,
                        7 => 24000,
                        8 => 32000,
                        9 => 44100,
                        10 => 48000,
                        11 => 96000,
                        15 => throw new InvalidDataException("The FLAC sample rate code is forbidden."),
                        _ => throw new InvalidOperationException()
                    };
                    break;
            }
            if (sampleRate == 0 || blockSize > configuration.MaximumBlockSize)
            {
                throw new InvalidDataException("The FLAC frame sample rate or block size is invalid.");
            }
            if (sampleRate != configuration.SampleRate)
            {
                throw new NotSupportedException("Changing FLAC sample rate within a stream is not supported.");
            }
            int crcOffset = reader.Position / 8;
            if (!reader.TryRead(8, out ulong crc))
            {
                return false;
            }
            if (crc != FlacCrc.Header(data[..crcOffset]))
            {
                throw new InvalidDataException("The FLAC frame header CRC does not match.");
            }
            header = new FlacFrameHeader(blockSize, assignment, variable, number, crcOffset + 1);
            return true;
        }

        private static bool TryReadNumber(ref FlacBitReader reader, bool variable, out long number)
        {
            number = 0;
            if (!reader.TryRead(8, out ulong first))
            {
                return false;
            }
            if (first < 0x80)
            {
                number = (long)first;
                return true;
            }
            int length = first switch
            {
                >= 0xc0 and <= 0xdf => 2,
                >= 0xe0 and <= 0xef => 3,
                >= 0xf0 and <= 0xf7 => 4,
                >= 0xf8 and <= 0xfb => 5,
                >= 0xfc and <= 0xfd => 6,
                0xfe when variable => 7,
                _ => throw new InvalidDataException("The FLAC frame/sample number prefix is invalid.")
            };
            number = (long)(first & (ulong)((1 << (7 - length)) - 1));
            for (int i = 1; i < length; i++)
            {
                if (!reader.TryRead(8, out ulong next))
                {
                    return false;
                }
                if ((next & 0xc0) != 0x80)
                {
                    throw new InvalidDataException("A FLAC frame/sample number continuation is invalid.");
                }
                number = (number << 6) | (long)(next & 63);
            }
            long minimum = length switch
            {
                2 => 0x80,
                3 => 0x800,
                4 => 0x10000,
                5 => 0x200000,
                6 => 0x4000000,
                7 => 0x80000000,
                _ => throw new InvalidOperationException()
            };
            if (number < minimum)
            {
                throw new InvalidDataException("The FLAC frame/sample number is overlong.");
            }
            return true;
        }
    }
}
