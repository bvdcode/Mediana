// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text;

namespace Mediana.Cli
{
    internal class HlsRenditionFiles(string directory, string name)
    {
        public string PlaylistUri => name + "/index.m3u8";

        public static HlsRenditionFiles Create(string root, string name)
        {
            string directory = Path.Combine(root, name);
            Directory.CreateDirectory(directory);
            return new(directory, name);
        }

        public ValueTask<Stream> OpenSegmentAsync(int number, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Stream destination = new FileStream(Path.Combine(directory, SegmentName(number)), FileMode.CreateNew,
                FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.Asynchronous);
            return ValueTask.FromResult(destination);
        }

        public Task WritePlaylistAsync(string playlist, CancellationToken token) =>
            File.WriteAllTextAsync(Path.Combine(directory, "index.m3u8"), playlist, new UTF8Encoding(false), token);

        public static string SegmentName(int number) => "segment-" + number.ToString(CultureInfo.InvariantCulture) + ".ts";
    }
}
