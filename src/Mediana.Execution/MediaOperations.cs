// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Execution
{
    /// <summary>Reusable media operations without file paths or console dependencies.
    /// Components are borrowed. Read callbacks transfer ownership of each frame to the operation.
    /// Successful completion finalizes the writer; failures leave cleanup to its owner.</summary>
    public static class MediaOperations
    {
        public static Task<MediaOperationProgress> EncodeAudioAsync(Func<CancellationToken, ValueTask<AudioFrame?>> read,
            IMediaEncoder<AudioFrame> encoder, IContainerWriter writer, Action<MediaOperationProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            EncodeAsync(read, encoder, writer, static frame => frame.Duration, progress, cancellationToken);

        public static Task<MediaOperationProgress> EncodeVideoAsync(Func<CancellationToken, ValueTask<VideoFrame?>> read,
            IMediaEncoder<VideoFrame> encoder, IContainerWriter writer, Action<MediaOperationProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            EncodeAsync(read, encoder, writer, static frame => frame.Duration, progress, cancellationToken);

        public static async Task<MediaOperationProgress> CopyAsync(IContainerReader reader, IContainerWriter writer,
            IReadOnlyCollection<int>? streamIndices = null, Action<MediaOperationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(writer);
            HashSet<int> selected = streamIndices is null ? reader.Streams.Select(stream => stream.StreamIndex).ToHashSet() : streamIndices.ToHashSet();
            if (selected.Count == 0 || selected.Any(index => !reader.Streams.Any(stream => stream.StreamIndex == index)))
            {
                throw new ArgumentException("Select at least one existing stream.", nameof(streamIndices));
            }
            long packets = 0;
            long bytes = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using MediaPacket? packet = await reader.ReadPacketAsync(cancellationToken).ConfigureAwait(false);
                if (packet is null)
                {
                    break;
                }
                if (selected.Contains(packet.StreamIndex))
                {
                    await writer.WritePacketAsync(packet, cancellationToken).ConfigureAwait(false);
                    packets = checked(packets + 1);
                    bytes = checked(bytes + packet.Data.Length);
                    progress?.Invoke(new(0, packets, bytes, null, false));
                }
            }
            await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
            MediaOperationProgress result = new(0, packets, bytes, null, true);
            progress?.Invoke(result);
            return result;
        }

        private static async Task<MediaOperationProgress> EncodeAsync<TFrame>(Func<CancellationToken, ValueTask<TFrame?>> read,
            IMediaEncoder<TFrame> encoder, IContainerWriter writer, Func<TFrame, MediaDuration?> duration,
            Action<MediaOperationProgress>? progress, CancellationToken token) where TFrame : class, IDisposable
        {
            ArgumentNullException.ThrowIfNull(read);
            ArgumentNullException.ThrowIfNull(encoder);
            ArgumentNullException.ThrowIfNull(writer);
            MediaEncodePipeline<TFrame> pipeline = new(encoder, writer);
            MediaDuration? processed = null;
            bool unknown = false;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                using TFrame? frame = await read(token).ConfigureAwait(false);
                if (frame is null)
                {
                    break;
                }
                await pipeline.WriteAsync(frame, token).ConfigureAwait(false);
                MediaDuration? span = duration(frame);
                if (span is null)
                {
                    unknown = true;
                }
                else if (!unknown)
                {
                    if (processed is MediaDuration previous)
                    {
                        if (previous.TimeBase != span.Value.TimeBase)
                        {
                            unknown = true;
                        }
                        else
                        {
                            processed = new(checked(previous.Value + span.Value.Value), previous.TimeBase);
                        }
                    }
                    else
                    {
                        processed = span;
                    }
                }
                progress?.Invoke(Snapshot(false));
            }
            await pipeline.CompleteInputAsync(token).ConfigureAwait(false);
            await writer.CompleteAsync(token).ConfigureAwait(false);
            MediaOperationProgress result = Snapshot(true);
            progress?.Invoke(result);
            return result;

            MediaOperationProgress Snapshot(bool complete) => new(pipeline.FramesSubmitted, pipeline.PacketsWritten,
                pipeline.PayloadBytes, unknown ? null : processed, complete);
        }
    }
}
