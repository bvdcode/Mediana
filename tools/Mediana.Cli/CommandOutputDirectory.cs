// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Cli
{
    internal static class CommandOutputDirectory
    {
        public static async Task WriteAsync(string outputPath, Func<string, Task> write, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destination = Path.GetFullPath(outputPath);
            if (Directory.Exists(destination) || File.Exists(destination))
            {
                throw new IOException("The output directory must be new.");
            }
            string parent = Path.GetDirectoryName(destination)!;
            if (!Directory.Exists(parent))
            {
                throw new DirectoryNotFoundException("The output directory's parent must exist.");
            }
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            Directory.CreateDirectory(temporary);
            try
            {
                await write(temporary);
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(temporary, destination);
            }
            finally
            {
                if (Directory.Exists(temporary))
                {
                    Directory.Delete(temporary, recursive: true);
                }
            }
        }
    }
}
