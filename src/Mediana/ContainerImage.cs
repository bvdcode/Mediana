// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana
{
    /// <summary>Owned original encoded image bytes selected by recognition, without image decoding.
    /// The lease survives container/source disposal; borrowed Data must not outlive this object.</summary>
    public class ContainerImage : IDisposable
    {
        private readonly MediaBuffer data;

        internal ContainerImage(ContainerImageSource source, int index, string decoderImplementationId, MediaBuffer data)
        {
            Source = source;
            Index = index;
            DecoderImplementationId = decoderImplementationId;
            this.data = data;
        }

        public ContainerImageSource Source { get; }
        /// <summary>Original zero-based index in the corresponding Pictures or Attachments collection.</summary>
        public int Index { get; }
        /// <summary>Registered image implementation recognizing the prefix; complete decoding can still fail.</summary>
        public string DecoderImplementationId { get; }
        public ReadOnlyMemory<byte> Data => data.Memory;
        public void Dispose() => data.Dispose();
    }
}
