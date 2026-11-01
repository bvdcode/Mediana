// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Cli
{
    internal class CommandOutputFile(string outputPath, bool overwrite = false)
    {
        private const int BufferBytes = 64 * 1024;

        private readonly string destination = Path.GetFullPath(outputPath);

        public async Task WriteAsync(Func<Stream, Task> write, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: BufferBytes, FileOptions.Asynchronous))
                {
                    await write(stream);
                }
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, destination, overwrite);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
    }
}
