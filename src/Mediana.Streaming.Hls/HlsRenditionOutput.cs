// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;

namespace Mediana.Streaming.Hls
{
    internal class HlsRenditionOutput(IHlsPackageOutput output, string name)
    {
        public string PlaylistUri => name + "/index.m3u8";
        public ValueTask<Stream> OpenSegmentAsync(int number, CancellationToken token) => output.OpenSegmentAsync(name, number, token);
        public ValueTask WritePlaylistAsync(string playlist, CancellationToken token) => output.WritePlaylistAsync(PlaylistUri, playlist, token);
        public static string SegmentName(int number) => "segment-" + number.ToString(CultureInfo.InvariantCulture) + ".ts";
    }
}
