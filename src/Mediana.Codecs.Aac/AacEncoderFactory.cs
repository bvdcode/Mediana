// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Codecs.Aac
{
    public class AacEncoderFactory(AacEncoderOptions? options = null) : IAudioEncoderFactory
    {
        public string ImplementationId => "mediana.aac.encoder";
        public string CodecId => "aac";
        public IMediaEncoder<AudioFrame> Create(AudioFormat format, TimeBase timeBase, MediaBufferAllocator allocator, int streamIndex = 0) =>
            AacEncoder.Create(format, timeBase, allocator, options, streamIndex);
    }
}
