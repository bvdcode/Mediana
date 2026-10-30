// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using ConsoleAppFramework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal static class CliApplication
    {
        public static async Task<int> RunAsync(string[] args, TextWriter? output = null, TextWriter? error = null,
            CancellationToken cancellationToken = default)
        {
            int previousExitCode = Environment.ExitCode;
            Action<string> previousLog = ConsoleApp.Log;
            Action<string> previousError = ConsoleApp.LogError;
            string? previousVersion = ConsoleApp.Version;
            IServiceProvider? previousServices = ConsoleApp.ServiceProvider;
            bool parserError = false;
            bool matchedCommand = false;
            ConsoleApp.Version = CliBuildInfo.Version;
            await (error ?? Console.Error).WriteLineAsync($"Mediana {CliBuildInfo.Version} | Copyright (c) 2026 Vadim Belov (bvdcode)");
            ConsoleApp.Log = (output ?? Console.Out).WriteLine;
            ConsoleApp.LogError = message =>
            {
                (error ?? Console.Error).WriteLine(message);
                parserError = true;
            };
            Environment.ExitCode = CliExitCodes.Success;
            try
            {
                ConsoleApp.ConsoleAppBuilder app = ConsoleApp.Create();
                app.ConfigureGlobalOptions((ref builder) =>
                {
                    matchedCommand = true;
                    LogLevel level = builder.AddGlobalOption<LogLevel>("--log-level",
                        "Minimum diagnostic level: Trace, Debug, Information, Warning, Error, Critical or None.", LogLevel.Information);
                    bool noColor = builder.AddGlobalOption<bool>("--no-color", "Disable colored diagnostic output.");
                    return new CliSettings(level, noColor);
                });
                app.UseFilter<CliLoggingFilter>();
                app.ConfigureServices((context, configuration, services) =>
                {
                    CliSettings settings = (CliSettings)context.GlobalOptions!;
                    services.AddLogging(builder => CliLogging.Configure(builder, settings.LogLevel, settings.NoColor));
                    services.AddSingleton(provider => provider.GetRequiredService<ILoggerFactory>().CreateLogger("mediana"));
                });
                app.Add<ConversionCommands>();
                app.Add<AudioCommands>("audio");
                app.Add<VideoCommands>("video");
                app.Add<ImageCommands>("image");
                app.Add<PdfCommands>("pdf");
                app.Add<ProbeCommands>("probe");
                app.Add<CoverCommands>("cover");
                app.Add<ContainerCommands>();
                app.Add<HlsCommands>("hls");
                await app.RunAsync(args.Length == 0 ? ["--help"] : args, cancellationToken);
                if (args.Length > 0 && !matchedCommand && !CliHelp.IsRequested(args))
                {
                    await (error ?? Console.Error).WriteLineAsync("Unknown or incomplete command. Run mediana --help.");
                    return CliExitCodes.Usage;
                }
                return parserError ? CliExitCodes.Usage : Environment.ExitCode;
            }
            finally
            {
                ConsoleApp.Log = previousLog;
                ConsoleApp.LogError = previousError;
                ConsoleApp.Version = previousVersion;
                ConsoleApp.ServiceProvider = previousServices;
                Environment.ExitCode = previousExitCode;
            }
        }
    }
}
