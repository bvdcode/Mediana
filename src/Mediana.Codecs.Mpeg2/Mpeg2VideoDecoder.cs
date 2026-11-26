// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Codecs.Mpeg2
{
    internal class Mpeg2VideoDecoder(EncodedStreamInfo stream, MediaBufferAllocator allocator)
        : IMediaCodec<MediaPacket, VideoFrame>
    {
        private Mpeg2Sequence? sequence;
        private VideoFrame? past;
        private VideoFrame? future;
        private VideoFrame? output;
        private MediaTimestamp? nextTime;
        private bool complete;

        public CodecInputStatus TrySend(MediaPacket input, CancellationToken cancellationToken = default)
        {
            if (output is not null)
            {
                return CodecInputStatus.OutputRequired;
            }
            VideoFrame frame = DecodePicture(input, cancellationToken, out Mpeg2PictureKind kind);
            if (kind == Mpeg2PictureKind.Bidirectional)
            {
                using (frame)
                {
                    Emit(frame);
                }
            }
            else
            {
                past?.Dispose();
                past = future;
                future = frame;
                if (past is not null)
                {
                    Emit(past);
                }
            }
            return CodecInputStatus.Accepted;
        }

        public CodecOutput<VideoFrame> Receive(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (output is VideoFrame frame)
            {
                output = null;
                return CodecOutput<VideoFrame>.Available(frame);
            }
            return complete ? CodecOutput<VideoFrame>.EndOfStream : CodecOutput<VideoFrame>.InputRequired;
        }

        public void CompleteInput()
        {
            if (!complete && future is not null)
            {
                Emit(future);
            }
            complete = true;
        }

        public void Reset()
        {
            Dispose();
            sequence = null;
            nextTime = null;
            complete = false;
        }

        public void Dispose()
        {
            output?.Dispose();
            past?.Dispose();
            future?.Dispose();
            output = past = future = null;
        }

        private VideoFrame DecodePicture(MediaPacket packet, CancellationToken token, out Mpeg2PictureKind kind)
        {
            Mpeg2StartCodes units = new(packet.Data.Span);
            Mpeg2PictureHeader? picture = null;
            WritableMediaBuffer? surface = null;
            int nextMacroblock = 0;
            try
            {
                while (units.TryRead(out byte code, out ReadOnlySpan<byte> payload))
                {
                    token.ThrowIfCancellationRequested();
                    switch (code)
                    {
                        case 0xb3 when picture is null:
                            sequence = Mpeg2Sequence.Read(payload);
                            break;
                        case 0x00 when picture is null:
                            picture = Mpeg2PictureHeader.Read(payload);
                            break;
                        case 0xb5 when surface is null:
                            ReadExtension(payload, picture);
                            break;
                        case >= 0x01 and <= 0xaf:
                            Mpeg2Sequence configuration = RequireSequence();
                            if (picture is not { HasCodingExtension: true })
                            {
                                throw new InvalidDataException("The MPEG-2 slice has no picture coding extension.");
                            }
                            surface ??= allocator.Allocate(configuration.SurfaceBytes);
                            nextMacroblock = ReadSlice(configuration, picture, surface, code, payload, nextMacroblock, token);
                            break;
                    }
                }

                Mpeg2Sequence decoded = RequireSequence();
                if (picture is null || surface is null || nextMacroblock != decoded.Columns * decoded.Rows)
                {
                    throw new InvalidDataException("The MPEG-2 picture does not cover the entire frame.");
                }
                kind = picture.Kind;
                using MediaBuffer pixels = surface.Publish();
                return new(decoded.Format(stream, picture.ScanMode), pixels, decoded.Planes(), packet.PresentationTime, decoded.FrameDuration);
            }
            finally
            {
                surface?.Dispose();
            }
        }

        private int ReadSlice(Mpeg2Sequence configuration, Mpeg2PictureHeader picture, WritableMediaBuffer surface,
            byte code, ReadOnlySpan<byte> payload, int nextMacroblock, CancellationToken token)
        {
            VideoFrame? forward = picture.Kind switch
            {
                Mpeg2PictureKind.Intra => null,
                Mpeg2PictureKind.Predictive => future,
                Mpeg2PictureKind.Bidirectional => past,
                _ => throw new InvalidOperationException("Unexpected MPEG-2 picture type.")
            };
            VideoFrame? backward = picture.Kind == Mpeg2PictureKind.Bidirectional ? future : null;
            Mpeg2SliceDecoder slices = new(configuration, picture, surface.Span,
                ReferencePixels(forward, configuration), ReferencePixels(backward, configuration));
            return slices.Read(code, payload, nextMacroblock, token);
        }

        private static ReadOnlySpan<byte> ReferencePixels(VideoFrame? reference, Mpeg2Sequence configuration)
        {
            if (reference is null)
            {
                return [];
            }
            if (reference.Format.Width != configuration.Width || reference.Format.Height != configuration.Height)
            {
                throw new InvalidDataException("The MPEG-2 reference picture dimensions changed.");
            }
            return reference.Data.Span;
        }

        private void ReadExtension(ReadOnlySpan<byte> payload, Mpeg2PictureHeader? picture)
        {
            Mpeg2BitReader bits = new(payload);
            switch (bits.Read(4))
            {
                case 1 when picture is null && sequence is not null:
                    sequence.ReadExtension(ref bits);
                    break;
                case 2 when picture is null:
                    RequireSequence().ReadDisplayExtension(ref bits);
                    break;
                case 3:
                    RequireSequence().Quantization.ReadExtension(ref bits);
                    break;
                case 5 or 9 or 10:
                    throw new NotSupportedException("MPEG-2 scalable video is not supported.");
                case 8 when picture is not null:
                    picture.ReadCodingExtension(ref bits);
                    break;
            }
        }

        private Mpeg2Sequence RequireSequence() => sequence is { Extended: true } current
            ? current : throw new InvalidDataException("The MPEG-2 picture has no extended sequence header.");

        private void Emit(VideoFrame frame)
        {
            MediaTimestamp? time = frame.Timestamp ?? nextTime;
            if (time is MediaTimestamp current && frame.Duration is MediaDuration duration)
            {
                TimeBase clock = TimeBase.Common(current.TimeBase, duration.TimeBase);
                long start = current.Rescale(clock, TimestampRounding.TowardZero).Value;
                long length = new MediaTimestamp(duration.Value, duration.TimeBase).Rescale(clock, TimestampRounding.TowardZero).Value;
                nextTime = new(checked(start + length), clock);
            }
            output = frame.WithTiming(time, frame.Duration);
        }
    }
}
