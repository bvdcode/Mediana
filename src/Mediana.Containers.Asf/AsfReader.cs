// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Containers.Asf
{
    internal class AsfReader : IContainerReader, IContainerStreamMetadataProvider
    {
        private readonly IRandomAccessByteSource source;
        private readonly MediaBufferAllocator allocator;
        private readonly AsfStream[] streams;
        private readonly AsfLayout layout;
        private int position;
        private int busy;
        private bool disposed;

        public AsfReader(IRandomAccessByteSource source, MediaBufferAllocator allocator, AsfLayout layout)
        {
            this.source = source;
            this.allocator = allocator;
            this.layout = layout;
            streams = layout.Streams.Values.OrderBy(item => item.Stream.StreamIndex).ToArray();
            Streams = Array.AsReadOnly(streams.Select(item => item.Stream).ToArray());
            Info = new("asf", layout.Properties.Duration, layout.Tags, streams.Select(Describe));
        }

        public ContainerInfo Info { get; }
        public IReadOnlyList<EncodedStreamInfo> Streams { get; }

        private ContainerStreamInfo Describe(AsfStream stream)
        {
            AsfStreamDescription description = layout.Descriptions.GetValueOrDefault(stream.Number);
            int languageIndex = description.LanguageIndex;
            string? language = description.Number != 0 && languageIndex < layout.Languages.Count ? layout.Languages[languageIndex] : null;
            if (string.IsNullOrWhiteSpace(language)) { language = null; }
            FrameRate? frameRate = stream.Stream.Kind == MediaKind.Video && description.FramePeriod > 0
                ? new(10_000_000, description.FramePeriod) : null;
            long? bitRate = description.BitRate != 0 ? description.BitRate : stream.DeclaredBitRate;
            return new(stream.Stream.StreamIndex, null, null, new(stream.Count, stream.Bytes), language,
                null, null, frameRate, bitRate);
        }

        public StreamMetadata? GetStreamMetadata(int streamIndex)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(streamIndex, streams.Length);
            return streams[streamIndex].Metadata;
        }

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                while (position < layout.Packets.Count)
                {
                    AsfMediaObject packet = layout.Packets[position];
                    if (packet.Length == 0)
                    {
                        position++;
                        continue;
                    }
                    using WritableMediaBuffer buffer = allocator.Allocate(packet.Length);
                    foreach (AsfPayload fragment in packet.Parts)
                    {
                        await source.ReadExactlyAtAsync(fragment.Position, buffer.Memory.Slice(fragment.Offset, fragment.Length),
                            cancellationToken).ConfigureAwait(false);
                    }
                    using MediaBuffer data = buffer.Publish();
                    MediaTimestamp? pts = packet.Timestamp is long time
                        ? new(time - layout.Properties.PrerollMilliseconds, AsfStream.Clock) : null;
                    MediaPacket result = new(packet.StreamIndex, data, pts, isKeyFrame: packet.KeyFrame);
                    position++;
                    return result;
                }
                return null;
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        public ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                disposed = true;
                layout.Packets.Clear();
                return ValueTask.CompletedTask;
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent ASF reader operations are not supported.");
            }
        }
    }
}
