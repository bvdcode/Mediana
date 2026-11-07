// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana
{
    internal class MediaTrackResources : IAsyncDisposable
    {
        private readonly List<Func<ValueTask>> release = [];

        public T Own<T>(T resource) where T : IDisposable
        {
            release.Add(() => { resource.Dispose(); return ValueTask.CompletedTask; });
            return resource;
        }

        public T OwnAsync<T>(T resource) where T : IAsyncDisposable
        {
            release.Add(resource.DisposeAsync);
            return resource;
        }

        public async ValueTask DisposeAsync()
        {
            await ReleaseAsync(release.Count - 1).ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        private async ValueTask ReleaseAsync(int index)
        {
            if (index < 0) { return; }
            Func<ValueTask> dispose = release[index];
            release.RemoveAt(index);
            try { await dispose().ConfigureAwait(false); }
            finally { await ReleaseAsync(index - 1).ConfigureAwait(false); }
        }
    }
}
