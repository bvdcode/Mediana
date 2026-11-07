// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana
{
    internal class VideoVisualSession(VideoSession session, IByteSource replay, MediaTimestamp? start) : VisualSession
    {
        private bool opening = true;
        public override bool IsImage => false;
        public override VideoDisplayMatrix DisplayMatrix => session.Stream.DisplayMatrix
            ?? throw new NotSupportedException("The video stream has no display transform interpretation.");
        public override MediaDuration? CodedDuration => session.Stream.CodedDuration;

        protected override async ValueTask<VideoFrame?> ReadCoreAsync(CancellationToken cancellationToken)
        {
            if (opening)
            {
                opening = false;
                if (start is MediaTimestamp target)
                { return await session.ReadFrameAtOrAfterAsync(target, cancellationToken).ConfigureAwait(false); }
            }
            return await session.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
        }

        protected override async ValueTask DisposeCoreAsync()
        {
            try { await session.DisposeAsync().ConfigureAwait(false); }
            finally { await replay.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
