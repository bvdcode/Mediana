// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli.Updates
{
    internal class UpdateRetryPolicy(ILogger logger, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        private const int MaximumAttempts = 6;
        private readonly Func<TimeSpan, CancellationToken, Task> wait = delay ?? Task.Delay;

        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token)
        {
            for (int attempt = 1; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    return await operation(token);
                }
                catch (Exception exception) when (attempt < MaximumAttempts && IsTransient(exception, token))
                {
                    TimeSpan interval = TimeSpan.FromSeconds(1 << (attempt - 1));
                    logger.LogWarning("Update request failed: {Reason}. Retrying in {Seconds} s (attempt {Attempt}/{MaximumAttempts}).",
                        exception.Message, interval.TotalSeconds, attempt + 1, MaximumAttempts);
                    await wait(interval, token);
                }
            }
        }

        private static bool IsTransient(Exception exception, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return false;
            }
            return exception switch
            {
                HttpRequestException request => request.StatusCode is null or HttpStatusCode.RequestTimeout or
                    HttpStatusCode.TooManyRequests || (int)request.StatusCode >= 500,
                HttpIOException => true,
                IOException { InnerException: SocketException } => true,
                OperationCanceledException => true,
                _ => false
            };
        }
    }
}
