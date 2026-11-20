// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Codecs.Mp3
{
    public class Mp3EncoderFactory(Mp3EncoderOptions? options = null) : IAudioEncoderFactory
    {
        public string ImplementationId => "mediana.mp3.encoder";
        public string CodecId => "mp3";
        public IMediaEncoder<AudioFrame> Create(AudioFormat format, TimeBase timeBase, MediaBufferAllocator allocator, int streamIndex = 0) =>
            Mp3Encoder.Create(format, timeBase, allocator, options, streamIndex);
    }
}
