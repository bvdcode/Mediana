namespace Mediana.Sample
{
    internal static class SampleOutputFile
    {
        public static async Task WriteAsync(string outputPath, Func<Stream, Task> write)
        {
            string destination = Path.GetFullPath(outputPath);
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: 4096, FileOptions.Asynchronous))
                {
                    await write(stream);
                }
                File.Move(temporary, destination);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
    }
}
