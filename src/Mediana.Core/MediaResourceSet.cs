// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core
{
    /// <summary>Immutable typed resource providers shared by nested media operations. Values remain caller-owned.
    /// Providers control caching and must support concurrent calls; no discovery or reflection is performed.</summary>
    public class MediaResourceSet
    {
        private readonly IReadOnlyDictionary<Type, object> providers;
        private MediaResourceSet(IReadOnlyDictionary<Type, object> providers) { this.providers = providers; }
        public static MediaResourceSet Empty { get; } = new(new Dictionary<Type, object>());

        public MediaResourceSet With<T>(T resource) where T : class
        {
            ArgumentNullException.ThrowIfNull(resource);
            return WithAsync<T>(_ => ValueTask.FromResult(resource));
        }

        public MediaResourceSet WithAsync<T>(Func<CancellationToken, ValueTask<T>> provider) where T : class
        {
            ArgumentNullException.ThrowIfNull(provider);
            Dictionary<Type, object> updated = new(providers) { [typeof(T)] = provider };
            return new(updated);
        }

        public async ValueTask<T?> GetAsync<T>(CancellationToken cancellationToken = default) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!providers.TryGetValue(typeof(T), out object? provider)) { return null; }
            T resource = await ((Func<CancellationToken, ValueTask<T>>)provider)(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return resource ?? throw new InvalidOperationException("A configured media resource provider returned null.");
        }
    }
}
