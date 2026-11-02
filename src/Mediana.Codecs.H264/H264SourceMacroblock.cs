// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal readonly ref struct H264SourceMacroblock
    {
        public const int ByteCount = 384;
        private readonly ReadOnlySpan<byte> samples;

        public H264SourceMacroblock(VideoFrame input, int x, int y, Span<byte> storage)
        {
            if (storage.Length != ByteCount)
            {
                throw new ArgumentException("The source macroblock requires 384 samples.", nameof(storage));
            }
            H264BorderExtension.Copy(input, 0, x, y, 16, 16, storage[..256]);
            H264BorderExtension.Copy(input, 1, x / 2, y / 2, 8, 8, storage.Slice(256, 64));
            H264BorderExtension.Copy(input, 2, x / 2, y / 2, 8, 8, storage.Slice(320, 64));
            samples = storage;
        }

        public ReadOnlySpan<byte> Samples => samples;
        public ReadOnlySpan<byte> Luma => samples[..256];
        public ReadOnlySpan<byte> Chroma => samples[256..];
    }
}
