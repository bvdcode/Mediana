namespace Mediana.Codecs.Flac
{
    internal class FlacSubframeDecoder
    {
        private bool headerRead;
        private int type;
        private int precision;
        private int wastedBits;
        private int order;
        private int shift;
        private FlacCoefficients coefficients;
        private int index;
        private int partitionSamples;
        private int partitions;
        private int parameterBits;
        private int remaining;
        private int riceParameter;
        private int escapeBits;
        private bool remainderPending;
        private uint quotient;

        public void Reset()
        {
            headerRead = false;
            index = 0;
            remaining = 0;
            partitions = 0;
            quotient = 0;
            remainderPending = false;
        }

        public bool TryRead(ref FlacBitReader reader, Span<long> samples, int bitsPerSample,
            CancellationToken cancellationToken)
        {
            if (!headerRead)
            {
                FlacBitReader start = reader;
                if (!TryReadHeader(ref start, samples, bitsPerSample, cancellationToken))
                {
                    return false;
                }
                reader = start;
                headerRead = true;
            }
            if (type == 0)
            {
                if (!reader.TryReadSigned(precision, out long constant))
                {
                    return false;
                }
                samples.Fill(constant << wastedBits);
                return true;
            }
            while (index < samples.Length)
            {
                if ((index & 255) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                long sample;
                if (type == 1)
                {
                    if (!reader.TryReadSigned(precision, out sample))
                    {
                        return false;
                    }
                }
                else
                {
                    if (remaining == 0 && !TryReadPartition(ref reader))
                    {
                        return false;
                    }
                    if (!TryReadResidual(ref reader, cancellationToken, out long residual))
                    {
                        return false;
                    }
                    long prediction = type >= 32 ? LinearPrediction(samples) : FixedPrediction(samples);
                    sample = prediction + residual;
                    ValidatePrecision(sample, precision);
                    remaining--;
                }
                samples[index++] = sample;
            }
            if (wastedBits > 0)
            {
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] <<= wastedBits;
                }
            }
            return true;
        }

        private bool TryReadHeader(ref FlacBitReader reader, Span<long> samples, int bitsPerSample,
            CancellationToken cancellationToken)
        {
            if (!reader.TryRead(8, out ulong header))
            {
                return false;
            }
            if ((header & 0x80) != 0)
            {
                throw new InvalidDataException("The FLAC subframe reserved bit must be zero.");
            }
            type = (int)(header >> 1);
            order = type switch
            {
                0 or 1 => 0,
                >= 8 and <= 12 => type - 8,
                >= 32 and <= 63 => type - 31,
                _ => throw new InvalidDataException("The FLAC subframe type is reserved.")
            };
            wastedBits = 0;
            if ((header & 1) != 0)
            {
                uint zeros = 0;
                if (!reader.TryReadUnary(ref zeros, (uint)bitsPerSample - 2, cancellationToken))
                {
                    return false;
                }
                wastedBits = (int)zeros + 1;
            }
            precision = bitsPerSample - wastedBits;
            if (order >= samples.Length && type is not (0 or 1))
            {
                throw new InvalidDataException("The FLAC predictor order must be smaller than the block size.");
            }
            for (int i = 0; i < order; i++)
            {
                if (!reader.TryReadSigned(precision, out long warmup))
                {
                    return false;
                }
                samples[i] = warmup;
            }
            if (type >= 32)
            {
                if (!reader.TryRead(4, out ulong coefficientBits) || !reader.TryReadSigned(5, out long rightShift))
                {
                    return false;
                }
                if (coefficientBits == 15 || rightShift < 0)
                {
                    throw new InvalidDataException("The FLAC predictor coefficient precision or shift is forbidden.");
                }
                shift = (int)rightShift;
                for (int i = 0; i < order; i++)
                {
                    if (!reader.TryReadSigned((int)coefficientBits + 1, out long coefficient))
                    {
                        return false;
                    }
                    coefficients[i] = (int)coefficient;
                }
            }
            index = order;
            if (type is not (0 or 1))
            {
                if (!reader.TryRead(2, out ulong method) || !reader.TryRead(4, out ulong partitionOrder))
                {
                    return false;
                }
                parameterBits = method switch
                {
                    0 => 4,
                    1 => 5,
                    _ => throw new InvalidDataException("The FLAC residual coding method is reserved.")
                };
                partitions = 1 << (int)partitionOrder;
                partitionSamples = samples.Length / partitions;
                if (samples.Length % partitions != 0 || partitionSamples <= order)
                {
                    throw new InvalidDataException("The FLAC residual partitions are incompatible with the predictor or block size.");
                }
            }
            return true;
        }

        private bool TryReadPartition(ref FlacBitReader reader)
        {
            if (partitions <= 0)
            {
                throw new InvalidDataException("The FLAC residual partitions ended prematurely.");
            }
            FlacBitReader start = reader;
            if (!start.TryRead(parameterBits, out ulong parameter))
            {
                return false;
            }
            escapeBits = -1;
            if (parameter == (ulong)((1 << parameterBits) - 1))
            {
                if (!start.TryRead(5, out ulong bits))
                {
                    return false;
                }
                escapeBits = (int)bits;
            }
            riceParameter = (int)parameter;
            remaining = partitionSamples - (index == order ? order : 0);
            partitions--;
            reader = start;
            return true;
        }

        private bool TryReadResidual(ref FlacBitReader reader, CancellationToken cancellationToken, out long residual)
        {
            residual = 0;
            if (escapeBits >= 0)
            {
                return reader.TryReadSigned(escapeBits, out residual);
            }
            if (!remainderPending)
            {
                if (!reader.TryReadUnary(ref quotient, (uint.MaxValue - 1) >> riceParameter, cancellationToken))
                {
                    return false;
                }
                remainderPending = true;
            }
            if (!reader.TryRead(riceParameter, out ulong low))
            {
                return false;
            }
            ulong folded = ((ulong)quotient << riceParameter) | low;
            if (folded > uint.MaxValue - 1)
            {
                throw new InvalidDataException("A FLAC residual exceeds the signed 32-bit range excluding its minimum value.");
            }
            residual = (long)(folded >> 1) ^ -(long)(folded & 1);
            quotient = 0;
            remainderPending = false;
            return true;
        }

        private long FixedPrediction(ReadOnlySpan<long> samples) => order switch
        {
            0 => 0,
            1 => samples[index - 1],
            2 => 2 * samples[index - 1] - samples[index - 2],
            3 => 3 * samples[index - 1] - 3 * samples[index - 2] + samples[index - 3],
            4 => 4 * samples[index - 1] - 6 * samples[index - 2] + 4 * samples[index - 3] - samples[index - 4],
            _ => throw new InvalidOperationException("The fixed FLAC predictor order is invalid.")
        };

        private long LinearPrediction(ReadOnlySpan<long> samples)
        {
            long sum = 0;
            for (int i = 0; i < order; i++)
            {
                sum += samples[index - i - 1] * coefficients[i];
            }
            return sum >> shift;
        }

        internal static void ValidatePrecision(long sample, int bits)
        {
            long limit = 1L << (bits - 1);
            if (sample < -limit || sample >= limit)
            {
                throw new InvalidDataException("A decoded FLAC sample exceeds its declared precision.");
            }
        }
    }
}
