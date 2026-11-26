// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.Ffv1
{
    internal record Ffv1FrameModel(int Version, bool Rice, VideoPixelFormat PixelFormat,
        Ffv1Quantizer Quantizer, byte[]? Transitions)
    {
        internal const int StateBytes = 32;

        internal static bool ReadKeyFrame(scoped ref Ffv1RangeReader reader)
        {
            byte state = 128;
            return reader.ReadBit(ref state);
        }

        internal static Ffv1FrameModel Read(scoped ref Ffv1RangeReader reader)
        {
            Span<byte> state = stackalloc byte[StateBytes];
            state.Fill(128);
            int version = reader.ReadSymbol(state);
            if (version is not (0 or 1))
            {
                throw new NotSupportedException("FFV1 in-band parameters require version 0 or 1.");
            }
            (bool rice, VideoPixelFormat format, byte[]? transitions) = ReadCoding(ref reader, state, version);
            return new(version, rice, format, ReadQuantizer(ref reader, state), transitions);
        }

        internal static (bool Rice, VideoPixelFormat Format, byte[]? Transitions) ReadCoding(
            scoped ref Ffv1RangeReader reader, scoped Span<byte> state, int version)
        {
            int coding = reader.ReadSymbol(state);
            if (coding is < 0 or > 2)
            {
                throw new InvalidDataException("Invalid FFV1 entropy coder.");
            }
            byte[]? transitions = null;
            if (coding == 2)
            {
                transitions = new byte[256];
                for (int i = 1; i < transitions.Length; i++)
                {
                    int transition = checked(Ffv1RangeReader.DefaultTransition(i) + reader.ReadSymbol(state, signed: true));
                    if (transition is < 1 or > 255)
                    {
                        throw new InvalidDataException("An FFV1 probability transition is outside its valid range.");
                    }
                    transitions[i] = (byte)transition;
                }
            }

            int colorSpace = reader.ReadSymbol(state);
            int depth = version == 0 ? 8 : reader.ReadSymbol(state);
            bool chroma = reader.ReadBit(ref state[0]);
            int horizontal = reader.ReadSymbol(state);
            int vertical = reader.ReadSymbol(state);
            bool alpha = reader.ReadBit(ref state[0]);
            if (colorSpace != 0 || depth is not (0 or 8) || !chroma || alpha)
            {
                throw new NotSupportedException("The FFV1 decoder supports 8-bit planar YUV without alpha.");
            }
            VideoPixelFormat pixelFormat = (horizontal, vertical) switch
            {
                (0, 0) => VideoPixelFormat.Yuv444P8,
                (1, 0) => VideoPixelFormat.Yuv422P8,
                (1, 1) => VideoPixelFormat.Yuv420P8,
                (2, 0) => VideoPixelFormat.Yuv411P8,
                _ => throw new NotSupportedException("The FFV1 chroma layout is not supported.")
            };
            return (coding == 0, pixelFormat, transitions);
        }

        internal static Ffv1Quantizer ReadQuantizer(scoped ref Ffv1RangeReader reader, scoped Span<byte> state)
        {
            short[] quantization = new short[5 * 256];
            int combinations = 1;
            for (int table = 0; table < 5; table++)
            {
                combinations = ReadQuantizer(ref reader, quantization.AsSpan(table * 256, 256), combinations, state);
            }
            return new(quantization, (combinations + 1) / 2);
        }

        private static int ReadQuantizer(scoped ref Ffv1RangeReader reader, scoped Span<short> table, int scale,
            scoped Span<byte> state)
        {
            state.Fill(128);
            int group = 0;
            for (int position = 0; position < 128; group++)
            {
                int repeat = reader.ReadSymbol(state);
                if (repeat >= 128 - position || group * scale > short.MaxValue)
                {
                    throw new InvalidDataException("An FFV1 context quantization table is invalid.");
                }
                int length = repeat + 1;
                table.Slice(position, length).Fill((short)(group * scale));
                position += length;
            }
            for (int i = 1; i < 128; i++)
            {
                table[256 - i] = (short)-table[i];
            }
            table[128] = (short)-table[127];
            int combined = scale * (2 * group - 1);
            if (combined > 32768)
            {
                throw new InvalidDataException("The FFV1 quantization table has too many contexts.");
            }
            return combined;
        }
    }
}
