// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>


namespace Mediana.Cli
{
    internal abstract class MediaCommands(CommandOutputPolicy outputs)
    {
        protected static Task<int> ExecuteAsync(string input, string? output, Func<Task<int>> execute)
        {
            ValidateInput(input);
            if (output is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(output);
                string destination = Path.GetFullPath(output);
                if (File.Exists(destination) || Directory.Exists(destination))
                {
                    throw new IOException("The output path already exists; choose a new path.");
                }
                if (!Directory.Exists(Path.GetDirectoryName(destination)))
                {
                    throw new DirectoryNotFoundException("The output path's parent directory must exist.");
                }
            }
            return execute();
        }

        protected async Task<int> ExecuteFileAsync(string input, string output, Func<CommandOutputFile, Task<int>> execute,
            CancellationToken cancellationToken = default)
        {
            ValidateInput(input);
            CommandOutputFile? file = await outputs.PrepareFileAsync(input, output, cancellationToken);
            if (file is null)
            {
                return CliExitCodes.Cancelled;
            }
            return await execute(file);
        }

        private static void ValidateInput(string input)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(input);
            if (!File.Exists(input))
            {
                throw new FileNotFoundException("Input media file does not exist.", input);
            }
        }

        protected static void ValidateGains(double[] gains)
        {
            if (gains.Length == 0 || gains.Any(value => !double.IsFinite(value)))
            {
                throw new ArgumentException("Supply one finite --gains value per input channel.", nameof(gains));
            }
        }
    }
}
