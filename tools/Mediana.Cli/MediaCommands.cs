// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>


namespace Mediana.Cli
{
    internal abstract class MediaCommands
    {
        protected static Task<int> ExecuteAsync(string input, string? output, Func<Task<int>> execute)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(input);
            if (!File.Exists(input))
            {
                throw new FileNotFoundException("Input media file does not exist.", input);
            }
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

        protected static void ValidateGains(double[] gains)
        {
            if (gains.Length == 0 || gains.Any(value => !double.IsFinite(value)))
            {
                throw new ArgumentException("Supply one finite --gains value per input channel.", nameof(gains));
            }
        }
    }
}
