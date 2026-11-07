// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana
{
    internal class ImageVisualSession(MediaEngine engine, MediaBuffer encoded, MediaBufferAllocator allocator,
        VisualOpenOptions options) : VisualSession
    {
        private IAsyncEnumerator<VideoFrame>? frames;
        private bool read;
        private CancellationTokenSource? lifetime;

        public override bool IsImage => true;
        public override VideoDisplayMatrix? DisplayMatrix => VideoDisplayMatrix.Identity;
        public override MediaDuration? CodedDuration => null;

        protected override async ValueTask<VideoFrame?> ReadCoreAsync(CancellationToken cancellationToken)
        {
            if (options.ReadImageSequence)
            {
                lifetime ??= new();
                using CancellationTokenRegistration registration = cancellationToken.Register(static state => ((CancellationTokenSource)state!).Cancel(), lifetime);
                frames ??= engine.DecodeImageFramesAsync(encoded.Memory, allocator, options.Request, options.Images,
                    lifetime.Token).GetAsyncEnumerator(lifetime.Token);
                return await frames.MoveNextAsync().ConfigureAwait(false) ? frames.Current : null;
            }
            if (read) { return null; }
            read = true;
            try
            {
                return await engine.DecodeImageAsync(encoded.Memory, allocator, options.Request, options.Images,
                    cancellationToken).ConfigureAwait(false);
            }
            finally { encoded.Dispose(); }
        }

        protected override async ValueTask DisposeCoreAsync()
        {
            try { if (frames is not null) { await frames.DisposeAsync().ConfigureAwait(false); } }
            finally { encoded.Dispose(); lifetime?.Dispose(); }
        }
    }
}
