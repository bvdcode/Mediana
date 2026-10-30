// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Execution;
using Mediana.IO;

namespace Mediana
{
    public partial class MediaEngine
    {
        /// <summary>Opens one demuxer and independent decoders for all audio/video streams or an explicit selection.
        /// Sequential sources are supported when the registered container reader supports them.
        /// Output preserves each stream's order; consumers coordinate presentation clocks themselves.</summary>
        public async ValueTask<MediaSession> OpenMediaAsync(IByteSource source, IReadOnlyCollection<int>? streamIndices = null,
            MediaOpenOptions? options = null, CancellationToken cancellationToken = default)
        {
            int[]? selected = streamIndices?.ToArray();
            if (selected is not null && (selected.Length == 0 || selected.Distinct().Count() != selected.Length || selected.Any(index => index < 0)))
            {
                throw new ArgumentException("Select unique nonnegative stream indices.", nameof(streamIndices));
            }
            options ??= new();
            ContainerSession container = await OpenContainerAsync(source, options, cancellationToken).ConfigureAwait(false);
            List<MediaDecodeTrack> tracks = [];
            MediaDecodeGroup? group = null;
            bool success = false;
            try
            {
                EncodedStreamInfo[] streams = selected is null
                    ? container.Streams.Where(stream => stream.Kind is MediaKind.Audio or MediaKind.Video).ToArray()
                    : selected.Select(index => container.Streams.SingleOrDefault(stream => stream.StreamIndex == index)
                        ?? throw new ArgumentException("The selected stream does not exist.", nameof(streamIndices))).ToArray();
                if (streams.Length == 0)
                {
                    throw new NotSupportedException("The input contains no selected audio or video streams.");
                }
                foreach (EncodedStreamInfo stream in streams)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    tracks.Add(stream.Kind switch
                    {
                        MediaKind.Audio => MediaDecodeTrack.Audio(stream, SelectDecoder(stream, audioDecoders).Create(stream, container.Allocator)),
                        MediaKind.Video => MediaDecodeTrack.Video(stream, SelectDecoder(stream, decoders).Create(stream, container.Allocator)),
                        _ => throw new NotSupportedException("The selected stream is not audio or video.")
                    });
                }
                group = new(container, tracks, options.MaxFrameReads);
                cancellationToken.ThrowIfCancellationRequested();
                MediaSession session = new(container, group, streams);
                success = true;
                return session;
            }
            finally
            {
                if (!success)
                {
                    try
                    {
                        if (group is not null)
                        {
                            group.Dispose();
                        }
                        else
                        {
                            foreach (MediaDecodeTrack track in tracks)
                            {
                                track.Dispose();
                            }
                        }
                    }
                    finally
                    {
                        await container.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }
    }
}
