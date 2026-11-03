// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Streaming.Hls;

namespace Mediana.Cli
{
    internal class HlsAudioOutput(AudioHlsWriter writer, HlsRenditionFiles files, string groupId) : IAsyncDisposable
    {
        public AudioHlsWriter Writer { get; } = writer;
        public string GroupId { get; } = groupId;

        public static HlsAudioOutput Create(EncodedStreamInfo stream, MediaBufferAllocator allocator,
            long targetMilliseconds, string root, int index)
        {
            string name = "audio-" + index.ToString(CultureInfo.InvariantCulture);
            HlsRenditionFiles files = HlsRenditionFiles.Create(root, name);
            AudioHlsWriter writer = AudioHlsWriter.Create(stream, files.OpenSegmentAsync, allocator,
                new() { TargetDuration = new(targetMilliseconds, new(1, 1000)) });
            return new(writer, files, name);
        }

        public async ValueTask<HlsAudioRendition> WritePlaylistAsync(long targetDurationSeconds, CancellationToken token)
        {
            await files.WritePlaylistAsync(Writer.CreateVodPlaylist(HlsRenditionFiles.SegmentName, targetDurationSeconds), token);
            return Writer.CreateRendition(files.PlaylistUri, GroupId, "Main", targetDurationSeconds) with
            {
                IsDefault = true,
                AutoSelect = true
            };
        }

        public ValueTask DisposeAsync() => Writer.DisposeAsync();
    }
}
