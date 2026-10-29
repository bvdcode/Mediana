// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs
{
    /// <summary>A synchronous, single-consumer state machine without I/O.
    /// Inputs are borrowed for the call; retained input needs its own lease.
    /// Output belongs to the caller. InputRequired must allow the next input to be accepted.
    /// CompleteInput drains delayed output; Reset discards state. Neither operation disposes caller-owned output.</summary>
    public interface IMediaCodec<in TInput, TOutput> : IDisposable
        where TInput : class, IDisposable
        where TOutput : class, IDisposable
    {
        CodecInputStatus TrySend(TInput input, CancellationToken cancellationToken = default);
        CodecOutput<TOutput> Receive(CancellationToken cancellationToken = default);
        void CompleteInput();
        void Reset();
    }
}
