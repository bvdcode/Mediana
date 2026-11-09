// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Streaming.Hls;
using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    internal class HlsAudioOutput(AudioHlsWriter writer, HlsRenditionOutput files, string groupId) : IAsyncDisposable
    {
        public AudioHlsWriter Writer { get; } = writer;
        public string GroupId { get; } = groupId;

        public static HlsAudioOutput Create(EncodedStreamInfo stream, MediaBufferAllocator allocator,
            MediaDuration targetDuration, IHlsPackageOutput output, int index, HlsBandwidthOptions bandwidth)
        {
            string name = "audio-" + index.ToString(CultureInfo.InvariantCulture);
            HlsRenditionOutput files = new(output, name);
            AudioHlsWriter writer = AudioHlsWriter.Create(stream, files.OpenSegmentAsync, allocator,
                new() { TargetDuration = targetDuration, Bandwidth = bandwidth });
            return new(writer, files, name);
        }

        public async ValueTask<HlsAudioRendition> WritePlaylistAsync(long targetDurationSeconds, CancellationToken token)
        {
            await files.WritePlaylistAsync(Writer.CreateVodPlaylist(HlsRenditionOutput.SegmentName, targetDurationSeconds), token).ConfigureAwait(false);
            return Writer.CreateRendition(files.PlaylistUri, GroupId, "Main", targetDurationSeconds) with
            {
                IsDefault = true,
                AutoSelect = true
            };
        }

        public ValueTask DisposeAsync() => Writer.DisposeAsync();
    }
}
