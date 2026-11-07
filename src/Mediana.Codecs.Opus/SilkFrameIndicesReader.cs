// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class SilkFrameIndicesReader
    {
        public static SilkFrameIndices Read(ref OpusRangeDecoder decoder, SilkFrameMode mode, SilkCodingMode coding,
            bool active, ref SilkEntropyHistory history, scoped SilkIndexBuffers buffers)
        {
            SilkNlsfCodebook codebook = new(mode.LpcOrder);
            ArgumentOutOfRangeException.ThrowIfLessThan(buffers.Gains.Length, mode.Subframes, nameof(buffers));
            ArgumentOutOfRangeException.ThrowIfLessThan(buffers.Lsf.Length, mode.LpcOrder + 1, nameof(buffers));
            ArgumentOutOfRangeException.ThrowIfLessThan(buffers.PitchFilters.Length, mode.Subframes, nameof(buffers));
            bool conditional = coding switch
            {
                SilkCodingMode.Independent or SilkCodingMode.IndependentWithoutLtpScaling => false,
                SilkCodingMode.Conditional => true,
                _ => throw new ArgumentOutOfRangeException(nameof(coding))
            };
            switch (history.Signal)
            {
                case SilkSignalType.Inactive:
                case SilkSignalType.Unvoiced:
                case SilkSignalType.Voiced:
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(history));
            }
            int type;
            if (active) { type = 2 + decoder.ReadInverseCdf([232, 158, 10, 0]); }
            else { type = decoder.ReadInverseCdf([230, 0]); }
            SilkSignalType signal = (type >> 1) switch
            {
                0 => SilkSignalType.Inactive,
                1 => SilkSignalType.Unvoiced,
                2 => SilkSignalType.Voiced,
                _ => throw new InvalidDataException("The SILK frame signal type is outside its entropy model.")
            };
            SilkGainReader.Read(ref decoder, buffers.Gains, mode.Subframes, signal, conditional);
            SilkNlsfReader.Read(ref decoder, codebook, signal, buffers.Lsf);
            byte interpolation = (byte)SilkNlsfReader.ReadInterpolation(ref decoder, mode.Subframes);
            SilkPitchIndices pitch = default;
            short lag = history.LagIndex;
            if (signal == SilkSignalType.Voiced)
            {
                pitch = SilkPitchReader.Read(ref decoder, mode, coding, history.Signal, history.LagIndex, buffers.PitchFilters);
                lag = pitch.LagIndex;
            }
            else { buffers.PitchFilters[..mode.Subframes].Clear(); }
            byte seed = (byte)decoder.ReadInverseCdf([192, 128, 64, 0]);
            history = new(signal, lag);
            return new(signal, (byte)(type & 1), interpolation, seed, pitch);
        }
    }
}
