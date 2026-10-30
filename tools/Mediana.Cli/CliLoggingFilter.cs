// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using ConsoleAppFramework;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal class CliLoggingFilter(ILogger logger, CliOperation operation, ConsoleAppFilter next) : ConsoleAppFilter(next)
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
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (context.CommandArguments is { IsEmpty: true } && context.CommandName is not ("update" or "--update" or "-u"))
                {
                    throw new ArgumentException("Input arguments are required. Run mediana " + context.CommandName + " --help.");
                }
                string name = context.CommandName.Trim();
                if (name.Length != 0)
                {
                    operation.Start(name);
                }
                await Next.InvokeAsync(context, cancellationToken);
                if (Environment.ExitCode == CliExitCodes.Success)
                {
                    operation.Complete();
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
