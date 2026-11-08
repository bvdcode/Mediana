// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1
{
    internal class Av1StoredPicture(Av1Picture picture) : IDisposable
    {
        internal Av1FrameHeader Header { get; } = picture.Header;
        internal MediaBuffer Pixels { get; } = picture.Pixels.Retain();
        internal bool Showable { get; set; } = picture.Header.Showable;
        public void Dispose() => Pixels.Dispose();
    }
}
