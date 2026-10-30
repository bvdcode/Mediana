// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Video;

namespace Mediana.Execution
{
    /// <summary>An owned decoded frame tagged with its source stream. Disposing releases its frame lease.
    /// A consumer retaining the audio/video value beyond this envelope must retain its own lease.</summary>
    public class DecodedMediaFrame : IDisposable
    {
        private DecodedMediaFrame(int streamIndex, AudioFrame? audio, VideoFrame? video, MediaKind kind)
        {
            StreamIndex = streamIndex;
            Audio = audio;
            Video = video;
            Kind = kind;
        }

        public int StreamIndex { get; }
        public MediaKind Kind { get; }
        public AudioFrame? Audio { get; }
        public VideoFrame? Video { get; }

        internal static DecodedMediaFrame OwnAudio(int index, AudioFrame frame) => new(index, frame, null, MediaKind.Audio);
        internal static DecodedMediaFrame OwnVideo(int index, VideoFrame frame) => new(index, null, frame, MediaKind.Video);
        public void Dispose()
        {
            Audio?.Dispose();
            Video?.Dispose();
        }
    }
}
