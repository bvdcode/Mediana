// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana
{
    public partial class MediaEngine
    {
        /// <summary>Recognizes registered image/document/animation or container video from content.
        /// Borrows the source and allocator; random-access input preserves its sequential cursor.
        /// Recognized decoder failures propagate without trying a different media interpretation.</summary>
        public async ValueTask<VisualSession> OpenVisualAsync(IByteSource source, MediaBufferAllocator allocator,
            VisualOpenOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new(); options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            int length = Math.Max(ImageProbeLength, containers.Select(item => item.ProbeLength).DefaultIfEmpty().Max());
            if (length > options.Media.MaxProbeBytes || ImageProbeLength > options.Images.MaxProbeBytes)
            { throw new NotSupportedException("Registered visual recognition exceeds the configured probe byte limit."); }
            using WritableMediaBuffer writable = allocator.Allocate(length);
            int read = await MediaInputProbe.ReadAsync(source, writable.Memory, cancellationToken).ConfigureAwait(false);
            IImageDecoder? image = FindImageDecoder(writable.Memory.Span[..read], source.Length ?? (read < length ? read : null));
            using MediaBuffer prefix = writable.Publish();
            IByteSource replay = MediaInputProbe.Replay(source, prefix, read);
            VideoSession? video = null;
            bool transferred = false;
            try
            {
                if (image is not null)
                {
                    if (options.StreamIndex is not null)
                    { throw new NotSupportedException("Image, document and animation input does not select container streams."); }
                    options.Request.Validate(image.Features);
                    MediaBuffer encoded = await EncodedImageInput.ReadAsync(replay, options.Images.MaxInputBytes, allocator, cancellationToken).ConfigureAwait(false);
                    return new ImageVisualSession(this, encoded, allocator, options);
                }
                if (options.Request.PageIndex != 0)
                { throw new NotSupportedException("Video input does not support document page selection."); }
                video = options.StreamIndex is int index
                    ? await OpenVideoAsync(replay, index, options.Media, cancellationToken).ConfigureAwait(false)
                    : await OpenVideoAsync(replay, options.Media, cancellationToken).ConfigureAwait(false);
                if (options.Request.Time is MediaTimestamp start && start.Value != 0)
                { await video.SeekAsync(start, cancellationToken).ConfigureAwait(false); }
                VisualSession session = new VideoVisualSession(video, replay, options.Request.Time);
                transferred = true;
                return session;
            }
            finally
            {
                if (!transferred)
                {
                    try { if (video is not null) { await video.DisposeAsync().ConfigureAwait(false); } }
                    finally { await replay.DisposeAsync().ConfigureAwait(false); }
                }
            }
        }
    }
}
