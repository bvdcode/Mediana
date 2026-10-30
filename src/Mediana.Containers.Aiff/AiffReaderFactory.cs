// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Aiff
{
    public class AiffReaderFactory : IContainerReaderFactory
    {
        private readonly AiffReadOptions options;

        public AiffReaderFactory(AiffReadOptions? options = null)
        {
            this.options = options ?? new AiffReadOptions();
            this.options.Validate();
        }

        public string ImplementationId => "aiff.managed";
        public int ProbeLength => 12;

        public bool CanRead(ReadOnlySpan<byte> prefix) => HasSignature(prefix);

        internal static bool HasSignature(ReadOnlySpan<byte> prefix) => prefix.Length >= 12 && prefix[..4].SequenceEqual("FORM"u8)
            && (prefix.Slice(8, 4).SequenceEqual("AIFF"u8) || prefix.Slice(8, 4).SequenceEqual("AIFC"u8));

        public async ValueTask<IContainerReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default) =>
            await AiffReader.OpenAsync(source, allocator, options, cancellationToken).ConfigureAwait(false);
    }
}
