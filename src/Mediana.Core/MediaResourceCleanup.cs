// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.ExceptionServices;

namespace Mediana.Core
{
    /// <summary>Releases every supplied resource and propagates all failures, including an optional operation failure.</summary>
    public static class MediaResourceCleanup
    {
        /// <summary>Releases synchronous dependents followed by their asynchronous owner, preserving every failure.</summary>
        public static async ValueTask DisposeAllAsync(IEnumerable<IDisposable> resources, IAsyncDisposable owner,
            Exception? operationFailure = null)
        {
            ArgumentNullException.ThrowIfNull(resources);
            ArgumentNullException.ThrowIfNull(owner);
            Exception? failure = null;
            try { DisposeAll(resources, operationFailure); }
            catch (Exception error) { failure = error; }
            try { await owner.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error)
            {
                failure = failure is null ? error : new AggregateException(failure, error).Flatten();
            }
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        public static void DisposeAll(IEnumerable<IDisposable> resources, Exception? operationFailure = null)
        {
            ArgumentNullException.ThrowIfNull(resources);
            List<Exception>? failures = operationFailure is null ? null : [operationFailure];
            foreach (IDisposable resource in resources)
            {
                try { resource.Dispose(); }
                catch (Exception error)
                {
                    (failures ??= []).Add(error);
                }
            }
            if (failures is { Count: 1 })
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            if (failures is { Count: > 1 })
            {
                throw new AggregateException("The operation or resource cleanup failed.", failures);
            }
        }
    }
}
