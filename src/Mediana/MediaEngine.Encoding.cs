// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Video;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana
{
    public partial class MediaEngine
    {
        private readonly Dictionary<string, IAudioEncoderFactory> audioEncoders;
        private readonly Dictionary<string, IVideoEncoderFactory> videoEncoders;
        private readonly Dictionary<string, IContainerWriterFactory> writers;

        public IMediaEncoder<AudioFrame> CreateAudioEncoder(string codecId, AudioFormat format, TimeBase timeBase,
            MediaBufferAllocator allocator, int streamIndex = 0) =>
            FindOutput(audioEncoders, codecId).Create(format, timeBase, allocator, streamIndex);

        public IMediaEncoder<VideoFrame> CreateVideoEncoder(string codecId, VideoFormat format, TimeBase timeBase,
            MediaBufferAllocator allocator, int streamIndex = 0) =>
            FindOutput(videoEncoders, codecId).Create(format, timeBase, allocator, streamIndex);

        /// <summary>Borrows the destination. Complete the returned writer to finalize output; disposal aborts unfinished output.</summary>
        public IContainerWriter CreateContainerWriter(string formatId, Stream destination,
            IReadOnlyList<EncodedStreamInfo> streams, MediaBufferAllocator allocator) =>
            FindOutput(writers, formatId).Create(destination, streams, allocator);

        private static T FindOutput<T>(Dictionary<string, T> registered, string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
            return registered.TryGetValue(identifier, out T? factory) ? factory :
                throw new NotSupportedException($"No output implementation is registered for '{identifier}'.");
        }
    }
}
