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
            : this(new H264SourcePicture(input), x, y, storage) { }

        public H264SourceMacroblock(scoped H264SourcePicture input, int x, int y, Span<byte> storage)
        {
            if (storage.Length != ByteCount)
            {
                throw new ArgumentException("The source macroblock requires 384 samples.", nameof(storage));
            }
            input.Read(x, y, storage);
            samples = storage;
        }

        public ReadOnlySpan<byte> Samples => samples;
        public ReadOnlySpan<byte> Luma => samples[..256];
        public ReadOnlySpan<byte> Chroma => samples[256..];
    }
}
