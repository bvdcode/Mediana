// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Cli
{
    internal class CommandOutputPolicy(bool overwrite, CommandConfirmation confirmation)
    {
        public async Task<CommandOutputFile?> PrepareFileAsync(string inputPath, string outputPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
            string destination = Path.GetFullPath(outputPath);
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (destination.Equals(Path.GetFullPath(inputPath), comparison))
            {
                throw new ArgumentException("Input and output must use different paths.", nameof(outputPath));
            }
            if (Directory.Exists(destination))
            {
                throw new IOException("The output file path refers to an existing directory.");
            }
            if (!Directory.Exists(Path.GetDirectoryName(destination)))
            {
                throw new DirectoryNotFoundException("The output path's parent directory must exist.");
            }
            bool replace = overwrite;
            if (File.Exists(destination) && !replace)
            {
                if (!confirmation.CanConfirm)
                {
                    throw new IOException("The output file already exists. Use --overwrite or -y to replace it.");
                }
                replace = await confirmation.ConfirmAsync($"File '{outputPath}' already exists. Overwrite?", cancellationToken);
                if (!replace)
                {
                    return null;
                }
            }
            return new CommandOutputFile(destination, replace);
        }
    }
}
