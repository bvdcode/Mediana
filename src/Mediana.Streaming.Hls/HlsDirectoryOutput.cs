// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;

namespace Mediana.Streaming.Hls
{
    /// <summary>Writes a package beneath an existing caller-owned directory, without replacing existing files.</summary>
    public class HlsDirectoryOutput : IHlsPackageOutput
    {
        private readonly string root;
        public HlsDirectoryOutput(string directory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            root = Path.GetFullPath(directory);
            if (!Directory.Exists(root)) { throw new DirectoryNotFoundException("The HLS output directory does not exist."); }
        }

        public ValueTask<Stream> OpenSegmentAsync(string rendition, int number, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentOutOfRangeException.ThrowIfNegative(number);
            string directory = Resolve(rendition);
            Directory.CreateDirectory(directory);
            Stream stream = new FileStream(Path.Combine(directory, HlsRenditionOutput.SegmentName(number)), FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
            return ValueTask.FromResult(stream);
        }

        public async ValueTask WritePlaylistAsync(string relativeUri, string playlist, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Resolve(relativeUri);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using FileStream output = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
            await using StreamWriter writer = new(output, new UTF8Encoding(false));
            await writer.WriteAsync(playlist.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        private string Resolve(string relative)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(relative);
            if (Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Contains(':')
                || relative.Split('/').Any(part => part is "" or "." or ".."))
            {
                throw new ArgumentException("An HLS output path must be relative to its package directory.", nameof(relative));
            }
            return Path.Combine(root, relative);
        }
    }
}
