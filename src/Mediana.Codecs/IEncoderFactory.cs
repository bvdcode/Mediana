// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Codecs
{
    /// <summary>Creates independent encoders. Creation validates input format, clock and module options before output I/O.</summary>
    public interface IEncoderFactory<TFrame, in TFormat> where TFrame : class, IDisposable
    {
        string ImplementationId { get; }
        string CodecId { get; }
        IMediaEncoder<TFrame> Create(TFormat format, TimeBase timeBase, MediaBufferAllocator allocator, int streamIndex = 0);
    }
}
