// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Images.Heif.Hevc
{
    internal readonly struct ByteSampleFormat : ISampleFormat<byte>
    {
        public int Read(byte sample) => sample;
        public byte Write(int sample) => (byte)sample;
    }
}
