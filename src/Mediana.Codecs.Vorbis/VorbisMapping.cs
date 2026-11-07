// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Vorbis
{
    internal class VorbisMapping : IDisposable
    {
        private readonly WritableMediaBuffer storage;
        private readonly int channels;
        public int SubmapCount { get; }
        public int CouplingSteps { get; }
        public ReadOnlySpan<byte> Multiplex => storage.Span[..channels];
        public ReadOnlySpan<byte> Floors => storage.Span.Slice(channels, SubmapCount);
        public ReadOnlySpan<byte> Residues => storage.Span.Slice(channels + SubmapCount, SubmapCount);
        public ReadOnlySpan<byte> Magnitudes => storage.Span.Slice(channels + 2 * SubmapCount, CouplingSteps);
        public ReadOnlySpan<byte> Angles => storage.Span[(channels + 2 * SubmapCount + CouplingSteps)..];

        public VorbisMapping(scoped VorbisMappingDefinition definition, MediaBufferAllocator allocator)
        {
            channels = definition.Multiplex.Length; SubmapCount = definition.Floors.Length; CouplingSteps = definition.Magnitude.Length;
            storage = allocator.Allocate(channels + 2 * SubmapCount + 2 * CouplingSteps);
            definition.Multiplex.CopyTo(storage.Span); definition.Floors.CopyTo(storage.Span[channels..]);
            definition.Residues.CopyTo(storage.Span[(channels + SubmapCount)..]);
            definition.Magnitude.CopyTo(storage.Span[(channels + 2 * SubmapCount)..]);
            definition.Angle.CopyTo(storage.Span[(channels + 2 * SubmapCount + CouplingSteps)..]);
        }

        public void Dispose() => storage.Dispose();
    }
}
