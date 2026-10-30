// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana
{
    internal partial class MediaDecodeSession<TFrame>
    {
        public bool CanSeek => pipeline.CanSeek;

        public async ValueTask SeekAsync(MediaTimestamp target, CancellationToken cancellationToken)
        {
            target.TimeBase.Validate();
            Enter();
            bool succeeded = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (faulted)
                {
                    throw new InvalidOperationException("The session has failed and must be disposed.");
                }
                await pipeline.SeekAsync(target, cancellationToken).ConfigureAwait(false);
                succeeded = true;
            }
            finally
            {
                try
                {
                    if (!succeeded)
                    {
                        faulted = true;
                        pipeline.Dispose();
                    }
                }
                finally
                {
                    Exit();
                }
            }
        }
    }
}
