// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Video;

namespace Mediana.Execution
{
    /// <summary>Configured decoder state for one stream. Ownership transfers to a successfully constructed decode group.</summary>
    public class MediaDecodeTrack : IDisposable
    {
        private readonly Func<CancellationToken, CodecOutput<DecodedMediaFrame>> receive;
        private readonly Func<MediaPacket, CancellationToken, CodecInputStatus> send;
        private readonly Action complete;
        private Action? dispose;

        private MediaDecodeTrack(EncodedStreamInfo stream, Func<CancellationToken, CodecOutput<DecodedMediaFrame>> receive,
            Func<MediaPacket, CancellationToken, CodecInputStatus> send, Action complete, Action dispose)
        {
            Stream = stream;
            this.receive = receive;
            this.send = send;
            this.complete = complete;
            this.dispose = dispose;
        }

        public EncodedStreamInfo Stream { get; }

        public static MediaDecodeTrack Audio(EncodedStreamInfo stream, IMediaCodec<MediaPacket, AudioFrame> decoder) =>
            Create(stream, decoder, MediaKind.Audio, DecodedMediaFrame.OwnAudio);

        public static MediaDecodeTrack Video(EncodedStreamInfo stream, IMediaCodec<MediaPacket, VideoFrame> decoder) =>
            Create(stream, decoder, MediaKind.Video, DecodedMediaFrame.OwnVideo);

        private static MediaDecodeTrack Create<TFrame>(EncodedStreamInfo stream, IMediaCodec<MediaPacket, TFrame> decoder,
            MediaKind kind, Func<int, TFrame, DecodedMediaFrame> wrap) where TFrame : class, IDisposable
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(decoder);
            if (stream.Kind != kind)
            {
                throw new ArgumentException("The decoder output kind differs from the selected stream.", nameof(stream));
            }
            return new(stream, token =>
            {
                CodecOutput<TFrame> result = decoder.Receive(token);
                return result.Status switch
                {
                    CodecOutputStatus.Available => CodecOutput<DecodedMediaFrame>.Available(wrap(stream.StreamIndex, result.Value)),
                    CodecOutputStatus.InputRequired => CodecOutput<DecodedMediaFrame>.InputRequired,
                    CodecOutputStatus.EndOfStream => CodecOutput<DecodedMediaFrame>.EndOfStream,
                    _ => throw new InvalidOperationException("Invalid decoder output state.")
                };
            }, decoder.TrySend, decoder.CompleteInput, decoder.Dispose);
        }

        internal CodecOutput<DecodedMediaFrame> Receive(CancellationToken token) => receive(token);
        internal CodecInputStatus Send(MediaPacket packet, CancellationToken token) => send(packet, token);
        internal void CompleteInput() => complete();
        public void Dispose() => Interlocked.Exchange(ref dispose, null)?.Invoke();
    }
}
