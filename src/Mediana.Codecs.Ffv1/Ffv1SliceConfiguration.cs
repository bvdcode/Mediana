// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Ffv1
{
    internal record Ffv1SliceConfiguration(int MinorVersion, bool Rice, VideoPixelFormat Format, byte[]? Transitions,
        int Columns, int Rows, Ffv1Quantizer[] Quantizers, bool HasCrc, bool IntraOnly)
    {
        public static Ffv1SliceConfiguration Read(Ffv1StreamConfiguration stream)
        {
            ReadOnlySpan<byte> data = stream.ExtraData.Span;
            if (data.Length < 6 || !Ffv1Crc.IsValid(data))
            {
                throw new InvalidDataException("The FFV1 configuration checksum is invalid.");
            }
            Ffv1RangeReader reader = new(data[..^4]);
            Span<byte> state = stackalloc byte[Ffv1FrameModel.StateBytes];
            state.Fill(128);
            int version = reader.ReadSymbol(state);
            if (version != 3)
            {
                throw new NotSupportedException("External FFV1 configuration records currently require version 3.");
            }
            int minor = reader.ReadSymbol(state);
            (bool rice, VideoPixelFormat format, byte[]? transitions) = Ffv1FrameModel.ReadCoding(ref reader, state, version);
            int columns = checked(reader.ReadSymbol(state) + 1);
            int rows = checked(reader.ReadSymbol(state) + 1);
            if (columns > stream.Width || rows > stream.Height || (long)columns * rows > 1024)
            {
                throw new NotSupportedException("The FFV1 slice grid exceeds the supported extent.");
            }
            int count = reader.ReadSymbol(state);
            if (count is < 1 or > 8) { throw new InvalidDataException("Invalid FFV1 quantizer count."); }
            Ffv1Quantizer[] quantizers = new Ffv1Quantizer[count];
            Span<byte> quantizerState = stackalloc byte[Ffv1FrameModel.StateBytes];
            for (int index = 0; index < count; index++)
            {
                quantizers[index] = Ffv1FrameModel.ReadQuantizer(ref reader, quantizerState);
            }
            Span<byte> initialState = stackalloc byte[Ffv1FrameModel.StateBytes * Ffv1FrameModel.StateBytes];
            initialState.Fill(128);
            for (int index = 0; index < count; index++)
            {
                if (reader.ReadBit(ref state[0]))
                {
                    quantizers[index].InitialRangeState = ReadInitialStates(ref reader, initialState, quantizers[index].ContextCount);
                }
            }
            int crcMode = reader.ReadSymbol(state);
            if (crcMode is not (0 or 1)) { throw new NotSupportedException("Unsupported FFV1 slice CRC mode."); }
            bool intra = minor > 2 && reader.ReadSymbol(state) switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidDataException("Invalid FFV1 intra-only flag.")
            };
            return new(minor, rice, format, transitions, columns, rows, quantizers, crcMode == 1, intra);
        }

        private static byte[] ReadInitialStates(scoped ref Ffv1RangeReader reader, scoped Span<byte> state, int contexts)
        {
            const int stride = Ffv1FrameModel.StateBytes;
            byte[] initial = new byte[checked(contexts * stride)];
            for (int index = 0; index < initial.Length; index++)
            {
                int prediction = index < stride ? 128 : initial[index - stride];
                int delta = reader.ReadSymbol(state.Slice((index % stride) * stride, stride), signed: true);
                byte value = unchecked((byte)(prediction + delta));
                if (value == 0) { throw new InvalidDataException("An FFV1 initial probability is zero."); }
                initial[index] = value;
            }
            return initial;
        }
    }
}
