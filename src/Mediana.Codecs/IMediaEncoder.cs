// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Codecs
{
    /// <summary>A codec with an immutable output stream declaration available before sending input.</summary>
    public interface IMediaEncoder<in TFrame> : IMediaCodec<TFrame, MediaPacket> where TFrame : class, IDisposable
    {
        EncodedStreamInfo Stream { get; }
    }
}
