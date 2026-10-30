// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal class BufferedVideoEncoder(MediaBufferAllocator allocator, bool invalidDrain = false) : IMediaEncoder<VideoFrame>
    {
        private readonly Queue<VideoFrame> frames = [];
        private bool completed;
        private int part;
        public bool IsDisposed { get; private set; }
        public EncodedStreamInfo Stream { get; } = new(0, MediaKind.Video, "fixture.split",
            new("none", "fixture.parts", EncodedPacketBoundary.ArbitraryBytes), new(1, 1));

        public CodecInputStatus TrySend(VideoFrame input, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (completed || frames.Count >= 2)
            {
                return CodecInputStatus.OutputRequired;
            }
            frames.Enqueue(input.Retain());
            return CodecInputStatus.Accepted;
        }

        public CodecOutput<MediaPacket> Receive(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (frames.Count > 1 || part != 0 || (completed && frames.Count != 0))
            {
                VideoFrame frame = frames.Peek();
                using WritableMediaBuffer data = allocator.Allocate(1);
                data.Memory.Span[0] = (byte)(frame.Data.Span[0] + part);
                using MediaBuffer buffer = data.Publish();
                MediaPacket packet = new(0, buffer, frame.Timestamp);
                if (++part == 2)
                {
                    frames.Dequeue().Dispose();
                    part = 0;
                }
                return CodecOutput<MediaPacket>.Available(packet);
            }
            return completed && !invalidDrain ? CodecOutput<MediaPacket>.EndOfStream : CodecOutput<MediaPacket>.InputRequired;
        }

        public void CompleteInput() => completed = true;
        public void Reset()
        {
            while (frames.TryDequeue(out VideoFrame? frame))
            {
                frame.Dispose();
            }
            completed = false;
            part = 0;
        }
        public void Dispose()
        {
            Reset();
            IsDisposed = true;
        }
    }
}
