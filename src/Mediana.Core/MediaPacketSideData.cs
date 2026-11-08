// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Core
{
    /// <summary>An independently owned immutable side-data lease identified by a format-defined kind.</summary>
    public class MediaPacketSideData : IDisposable
    {
        private readonly MediaBuffer buffer;

        public MediaPacketSideData(string kind, MediaBuffer buffer)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(kind);
            ArgumentNullException.ThrowIfNull(buffer);
            Kind = kind;
            this.buffer = buffer.Retain();
        }

        public string Kind { get; }
        public ReadOnlyMemory<byte> Data => buffer.Memory;
        public MediaBuffer RetainData() => buffer.Retain();
        public MediaPacketSideData Retain() => new(Kind, buffer);
        public void Dispose() => buffer.Dispose();
    }
}
