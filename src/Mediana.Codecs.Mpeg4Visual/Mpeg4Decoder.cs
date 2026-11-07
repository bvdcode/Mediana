// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual
{
    internal class Mpeg4Decoder : IMediaCodec<MediaPacket, VideoFrame>
    {
        private readonly EncodedStreamInfo stream;
        private readonly MediaBufferAllocator allocator;
        private Mpeg4VisualConfiguration? configuration;
        private ReadOnlyMemory<byte> layer;
        private VideoColorInfo color = new(Range: ColorRange.Limited, ChromaLocation: ChromaLocation.Left);
        private Mpeg4PictureDecoder? pictures;
        private VideoFormat? format;
        private PlaneLayout[]? planes;
        private VideoFrameReorderQueue frames = new();
        private bool completed, disposed, faulted;
        private int busy;
        internal Mpeg4Decoder(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            this.stream = stream; this.allocator = allocator; ReadConfiguration(stream.Configuration);
        }
        public CodecInputStatus TrySend(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            Enter(); bool success = false;
            try
            {
                EnsureUsable(); ArgumentNullException.ThrowIfNull(packet); cancellationToken.ThrowIfCancellationRequested();
                if (completed) { throw new InvalidOperationException("MPEG-4 Visual input is complete."); }
                if (frames.HasReady) { success = true; return CodecInputStatus.OutputRequired; }
                if (packet.StreamIndex != stream.StreamIndex) { throw new ArgumentException("The packet belongs to a different MPEG-4 Visual stream.", nameof(packet)); }
                ReadOnlySpan<byte> data = packet.Data.Span; int picture = -1;
                ReadConfiguration(packet.Data);
                for (int at = Mpeg4VisualStartCodes.Find(data); at >= 0; at = Mpeg4VisualStartCodes.Find(data, at + 4))
                {
                    if (data[at + 3] == 0xb6)
                    {
                        if (picture >= 0) { throw new NotSupportedException("An MPEG-4 Visual access unit must contain one VOP."); }
                        picture = at;
                    }
                }
                if (configuration is null || picture < 0) { throw new InvalidDataException("The MPEG-4 Visual access unit requires a layer header and one VOP."); }
                EnsurePictures();
                for (int at = Mpeg4VisualStartCodes.Find(data); at >= 0 && at < picture; at = Mpeg4VisualStartCodes.Find(data, at + 4))
                { if (data[at + 3] == 0xb3) { pictures!.ReadGroup(data[(at + 4)..]); } }
                using MediaBuffer pixels = pictures!.Decode(data[(picture + 4)..], out Mpeg4VopHeader header, cancellationToken);
                if (header.IsCoded)
                {
                    MediaTimestamp timestamp = packet.PresentationTime ?? new(pictures.PictureTime, new(1, configuration.TimeIncrementResolution));
                    VideoFrame frame = new(format!, pixels, planes!, timestamp, packet.Duration); bool queued = false;
                    try { frames.Add(frame, pictures.PictureTime, configuration.LowDelay ? 0 : 1); queued = true; }
                    finally { if (!queued) { frame.Dispose(); } }
                }
                success = true; return CodecInputStatus.Accepted;
            }
            finally { Finish(success); }
        }
        private void ReadConfiguration(ReadOnlyMemory<byte> data)
        {
            for (int at = Mpeg4VisualStartCodes.Find(data.Span); at >= 0; at = Mpeg4VisualStartCodes.Find(data.Span, at + 4))
            {
                int next = Mpeg4VisualStartCodes.Find(data.Span, at + 4), end = next < 0 ? data.Length : next;
                if (data.Span[at + 3] == 0xb6) { return; }
                if (data.Span[at + 3] == 0xb5) { color = Mpeg4VisualSignal.Read(data.Span[(at + 4)..end]); format = null; }
                if (data.Span[at + 3] is not (>= 0x20 and <= 0x2f)) { continue; }
                ReadOnlyMemory<byte> incoming = data[at..end];
                if (!incoming.Span.SequenceEqual(layer.Span))
                {
                    configuration = Mpeg4VisualConfiguration.Read(incoming.Span); layer = incoming.ToArray();
                    pictures?.Dispose(); pictures = null; format = null;
                }
            }
        }
        private void EnsurePictures()
        {
            Mpeg4VisualConfiguration config = configuration!;
            pictures ??= new(config, allocator);
            if (format is not null) { return; }
            SampleAspectRatio aspect = new(config.AspectNumerator, config.AspectDenominator);
            format = new(config.Width, config.Height, VideoPixelFormat.Yuv420P8, color,
                stream.AspectRatioDescription?.Resolve(aspect) ?? aspect, stream.ColorDescription);
            int luma = pictures.Width * pictures.Height;
            planes = [new(0, pictures.Width, config.Width, config.Height),
                new(luma, pictures.Width / 2, (config.Width + 1) / 2, (config.Height + 1) / 2),
                new(luma + luma / 4, pictures.Width / 2, (config.Width + 1) / 2, (config.Height + 1) / 2)];
        }
        public CodecOutput<VideoFrame> Receive(CancellationToken cancellationToken = default)
        {
            Enter(); bool success = false;
            try
            {
                EnsureUsable(); cancellationToken.ThrowIfCancellationRequested(); VideoFrame? frame = frames.Receive();
                CodecOutput<VideoFrame> result = frame is not null ? CodecOutput<VideoFrame>.Available(frame)
                    : completed ? CodecOutput<VideoFrame>.EndOfStream : CodecOutput<VideoFrame>.InputRequired;
                success = true; return result;
            }
            finally { Finish(success); }
        }
        public void CompleteInput()
        {
            Enter(); bool success = false;
            try { EnsureUsable(); frames.Drain(); pictures?.Dispose(); pictures = null; completed = true; success = true; }
            finally { Finish(success); }
        }
        public void Reset()
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this); Release(); frames = new(); completed = faulted = false;
                configuration = null; layer = default; color = new(Range: ColorRange.Limited, ChromaLocation: ChromaLocation.Left);
                ReadConfiguration(stream.Configuration);
            }
            finally { Volatile.Write(ref busy, 0); }
        }
        public void Dispose() { Enter(); try { disposed = true; Release(); } finally { Volatile.Write(ref busy, 0); } }
        private void Release() { frames.Dispose(); pictures?.Dispose(); pictures = null; format = null; planes = null; }
        private void Finish(bool success)
        { try { if (!success) { faulted = true; Release(); } } finally { Volatile.Write(ref busy, 0); } }
        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) { throw new InvalidOperationException("The MPEG-4 Visual decoder requires reset or disposal after a failure."); }
        }
        private void Enter()
        { if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) { throw new InvalidOperationException("Concurrent MPEG-4 Visual codec operations are not supported."); } }
    }
}
