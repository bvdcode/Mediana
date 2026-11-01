// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Containers.Matroska
{
    public partial class MatroskaReader
    {
        public static async ValueTask<MatroskaReader> OpenAsync(IRandomAccessByteSource source,
            MediaBufferAllocator allocator, MatroskaReadOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new();
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            (MatroskaHeader header, EbmlElement segment, MatroskaSegmentMetadata metadata,
                IReadOnlyList<MatroskaTrack> tracks, IReadOnlyList<MatroskaAttachmentInfo> attachments) =
                await ReadHeadersAsync(source, allocator, options, cancellationToken).ConfigureAwait(false);
            MatroskaSegmentInfo info = metadata.Info;
            MatroskaPacketIndex? index = null;
            MatroskaPacketSource? packets = null;
            bool success = false;
            try
            {
                index = await MatroskaPacketIndexBuilder.ReadAsync(source, allocator, segment, tracks,
                    options.IndexOptions, options.MetadataLimits, header.MaximumSizeWidth, cancellationToken).ConfigureAwait(false);
                IReadOnlyList<MatroskaCodecConfiguration> configurations = await MatroskaCodecReader.ReadAsync(
                    source, allocator, tracks, index, options.IndexOptions, options.MaxCodecConfigurationBytes,
                    options.MaxTotalCodecConfigurationBytes, cancellationToken).ConfigureAwait(false);
                IReadOnlyList<MatroskaTrackInfo> descriptions = Array.AsReadOnly(tracks.Select((track, stream) =>
                    new MatroskaTrackInfo(track, configurations[stream].Stream)).ToArray());
                ContainerInfo commonInfo = CreateInfo(metadata, descriptions, index);
                packets = new(source, allocator, index, tracks, configurations, info.TimestampScale, options.IndexOptions);
                cancellationToken.ThrowIfCancellationRequested();
                MatroskaAttachmentSource attachmentSource = new(source, allocator, attachments, options.MaxAttachmentBytes);
                MatroskaReader reader = new(index, packets, info, descriptions, commonInfo, attachmentSource);
                success = true;
                return reader;
            }
            finally
            {
                if (!success)
                {
                    packets?.Dispose();
                    index?.Dispose();
                }
            }
        }

        private static async ValueTask<(MatroskaHeader Header, EbmlElement Segment, MatroskaSegmentMetadata Metadata,
            IReadOnlyList<MatroskaTrack> Tracks, IReadOnlyList<MatroskaAttachmentInfo> Attachments)> ReadHeadersAsync(IRandomAccessByteSource source,
            MediaBufferAllocator allocator, MatroskaReadOptions options, CancellationToken token)
        {
            MatroskaHeader header = await MatroskaHeaderReader.ReadAsync(source, allocator, options.MaxHeaderBytes, token).ConfigureAwait(false);
            MatroskaElementBudget elementBudget = new(options.MaxElements);
            using MatroskaSegmentReader segments = new(source, allocator, header.MaximumSizeWidth, elementBudget);
            using MatroskaMetadataReader metadata = new(source, allocator, options.MetadataLimits, header.MaximumSizeWidth, elementBudget);
            EbmlElement segment = await segments.ReadRootAsync(header.BodyOffset, token).ConfigureAwait(false);
            EbmlElement? infoElement = null;
            EbmlElement? tracksElement = null;
            MatroskaSegmentMetadata? info = null;
            IReadOnlyList<MatroskaTrack>? tracks = null;
            IReadOnlyList<MatroskaAttachmentInfo>? attachments = null;
            MatroskaMetadataByteBudget byteBudget = new(options.MaxMetadataBytes);
            long offset = segment.DataOffset;
            while (await segments.ReadChildAsync(segment, offset, token).ConfigureAwait(false) is EbmlElement child)
            {
                if (child.Id is 0x1549A966 or 0x1654AE6B)
                {
                    byteBudget.Reserve(child.DataSize!.Value);
                }
                switch (child.Id)
                {
                    case 0x1549A966:
                        if (infoElement is EbmlElement originalInfo)
                        {
                            await RequireIdenticalAsync(source, allocator, originalInfo, child, token).ConfigureAwait(false);
                        }
                        else
                        {
                            info = await MatroskaInfoReader.ReadMetadataAsync(metadata, child, token).ConfigureAwait(false);
                            infoElement = child;
                        }
                        break;
                    case 0x1654AE6B:
                        if (tracksElement is EbmlElement originalTracks)
                        {
                            await RequireIdenticalAsync(source, allocator, originalTracks, child, token).ConfigureAwait(false);
                        }
                        else
                        {
                            tracks = await MatroskaTracksReader.ReadAsync(metadata, child, token).ConfigureAwait(false);
                            tracksElement = child;
                        }
                        break;
                    case 0xBF:
                        if (offset != segment.DataOffset || child.DataSize != sizeof(uint))
                        {
                            throw new InvalidDataException("A Segment CRC-32 must be first and contain four bytes.");
                        }
                        if (options.VerifyMediaChecksums)
                        {
                            await metadata.ReadChildAsync(segment with { EndOffset = segment.EndOffset ?? source.Length }, offset, token).ConfigureAwait(false);
                        }
                        break;
                    case 0x1941A469:
                        if (attachments is not null)
                        {
                            throw new InvalidDataException("Matroska Attachments must not be repeated.");
                        }
                        attachments = await MatroskaAttachmentsReader.ReadAsync(metadata, child, options.MaxAttachments,
                            byteBudget, options.VerifyMediaChecksums, token).ConfigureAwait(false);
                        break;
                }
                offset = child.EndOffset!.Value;
            }
            await segments.VerifyDocumentEndAsync(segment, token).ConfigureAwait(false);
            return (header, segment, info ?? throw new InvalidDataException("Matroska Info is missing."),
                tracks ?? throw new InvalidDataException("Matroska Tracks is missing."), attachments ?? Array.Empty<MatroskaAttachmentInfo>());
        }

        private static async ValueTask RequireIdenticalAsync(IRandomAccessByteSource source, MediaBufferAllocator allocator,
            EbmlElement first, EbmlElement next, CancellationToken token)
        {
            if (first.DataSize != next.DataSize)
            {
                throw new NotSupportedException("Differently encoded recurring Matroska metadata is not supported.");
            }
            const int compareBytes = 4096;
            using WritableMediaBuffer storage = allocator.Allocate(2 * compareBytes);
            long offset = 0;
            long length = first.DataSize!.Value;
            while (offset < length)
            {
                int count = (int)Math.Min(compareBytes, length - offset);
                Memory<byte> original = storage.Memory[..count];
                Memory<byte> repeated = storage.Memory.Slice(compareBytes, count);
                await source.ReadExactlyAtAsync(first.DataOffset + offset, original, token).ConfigureAwait(false);
                await source.ReadExactlyAtAsync(next.DataOffset + offset, repeated, token).ConfigureAwait(false);
                if (!original.Span.SequenceEqual(repeated.Span))
                {
                    throw new NotSupportedException("Differently encoded recurring Matroska metadata is not supported.");
                }
                offset += count;
            }
        }

        private static ContainerInfo CreateInfo(MatroskaSegmentMetadata metadata, IReadOnlyList<MatroskaTrackInfo> tracks,
            MatroskaPacketIndex index)
        {
            MatroskaSegmentInfo info = metadata.Info;
            MediaDuration? declared = info.Duration is double ticks ? MatroskaTrackClock.GetSegmentDuration(ticks, info.TimestampScale) : null;
            MediaDateTimeTag[] dates = [];
            if (info.DateNanoseconds is long nanoseconds)
            {
                DateTime epoch = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                long calendarTicks = decimal.ToInt64(decimal.Round(nanoseconds / 100m, 0, MidpointRounding.ToEven));
                dates = [new("matroska", "DateUTC", epoch.AddTicks(calendarTicks), "Segment/Info")];
            }
            ContainerStreamInfo[] streams = new ContainerStreamInfo[tracks.Count];
            for (int stream = 0; stream < tracks.Count; stream++)
            {
                MatroskaTrackInfo track = tracks[stream];
                MediaTag[] tags = track.Labels.Name is string name ?
                    [new("matroska", "Name", name, sourcePath: "Segment/Tracks/TrackEntry")] : [];
                long count = index.GetPacketCount(stream);
                MediaDuration? duration = count == 0 ? new(0, MatroskaTrackClock.TimeBase) : null;
                streams[stream] = new(stream, tags: tags,
                    packetIndex: new(count, index.GetPayloadBytes(stream), duration),
                    language: track.Labels.LanguageBcp47 ?? track.Labels.Language);
            }
            return new("matroska", declared, metadata.Tags, streams, dates);
        }
    }
}
