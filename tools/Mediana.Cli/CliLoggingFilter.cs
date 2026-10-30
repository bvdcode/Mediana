// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using ConsoleAppFramework;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal class CliLoggingFilter(ILogger logger, ConsoleAppFilter next) : ConsoleAppFilter(next)
    {
        public override async Task InvokeAsync(ConsoleAppContext context, CancellationToken cancellationToken)
        {
            if (CliHelp.IsRequested(context.Arguments))
            {
                await Next.InvokeAsync(context, cancellationToken);
                return;
            }
            CliSettings settings = (CliSettings)context.GlobalOptions!;
            if (settings.LogLevel is < LogLevel.Trace or > LogLevel.None)
            {
                ConsoleApp.LogError("Use a named --log-level value from Trace to None.");
                Environment.ExitCode = CliExitCodes.Usage;
                return;
            }
            long start = Stopwatch.GetTimestamp();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (context.CommandArguments is { IsEmpty: true })
                {
                    throw new ArgumentException("Input arguments are required. Run mediana " + context.CommandName + " --help.");
                }
                string operation = context.CommandName;
                if (string.IsNullOrEmpty(operation))
                {
                    operation = "conversion";
                }
                CliLog.Starting(logger, operation);
                await Next.InvokeAsync(context, cancellationToken);
                if (Environment.ExitCode == CliExitCodes.Success)
                {
                    CliLog.Completed(logger, operation, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CliLog.Cancelled(logger);
                Environment.ExitCode = CliExitCodes.Cancelled;
            }
            catch (ArgumentParseFailedException exception)
            {
                Report(logger, exception, CliExitCodes.Usage);
            }
            catch (NotSupportedException exception)
            {
                Report(logger, exception, CliExitCodes.Unsupported);
            }
            catch (InvalidDataException exception)
            {
                Report(logger, exception, CliExitCodes.InvalidInput);
            }
            catch (ArgumentException exception)
            {
                Report(logger, exception, CliExitCodes.Usage);
            }
            catch (IOException exception)
            {
                Report(logger, exception, CliExitCodes.IoFailure);
            }
            catch (UnauthorizedAccessException exception)
            {
                Report(logger, exception, CliExitCodes.IoFailure);
            }
            catch (Exception exception)
            {
                Report(logger, exception, CliExitCodes.Failure);
            }
        }

        private static void Report(ILogger logger, Exception exception, int exitCode)
        {
            CliLog.Failed(logger, exception.Message);
            CliLog.FailureDetails(logger, exception);
            Environment.ExitCode = exitCode;
        }
    }
}
