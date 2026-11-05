// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Execution;

namespace Mediana.Streaming.Hls
{
    /// <summary>Encodes an aligned H.264 rendition ladder, coordinates audio delivery and writes a complete VOD package.
    /// Input sessions, audio sources, output and allocator remain borrowed. Consumed frames and created writers are disposed.</summary>
    public static class HlsRenditionEncoder
    {
        public static ValueTask<HlsEncodingResult> EncodeAsync(VideoSession session, IHlsPackageOutput output,
            HlsRenditionOptions options, MediaBufferAllocator allocator, HlsAudioSource? audio = null,
            Action<HlsEncodingProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            return EncodeAsync(ReadAsync(session, cancellationToken), session.Stream, output, options, allocator, audio, progress, cancellationToken);
        }

        public static async ValueTask<HlsEncodingResult> EncodeAsync(IAsyncEnumerable<VideoFrame> frames, EncodedStreamInfo stream,
            IHlsPackageOutput output, HlsRenditionOptions options, MediaBufferAllocator allocator, HlsAudioSource? audio = null,
            Action<HlsEncodingProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(frames);
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentNullException.ThrowIfNull(options.VideoProfiles);
            cancellationToken.ThrowIfCancellationRequested();
            HlsVideoProfile[] profiles = options.VideoProfiles.ToArray();
            int[] audioProfiles = options.AudioProfileIndices?.ToArray() ?? new int[profiles.Length];
            Validate(stream, options, profiles, audioProfiles, audio);
            List<HlsVideoRendition> video = [];
            List<HlsAudioOutput> audioOutputs = [];
            MediaDurationAccumulator processed = new();
            long count = 0;
            await using IAsyncEnumerator<VideoFrame> input = frames.GetAsyncEnumerator(cancellationToken);
            if (!await input.MoveNextAsync().ConfigureAwait(false)) { throw new InvalidDataException("The input contains no decoded video frames."); }
            VideoFrame? current = input.Current;
            try
            {
                if (options.SeparateAudio)
                {
                    for (int profile = 0; profile < audio!.ProfileCount; profile++)
                    {
                        audioOutputs.Add(HlsAudioOutput.Create(audio.GetStream(profile), allocator, options.TargetDuration, output, profile));
                    }
                }
                HashSet<(int Width, int Height)> dimensions = [];
                for (int index = 0; index < profiles.Length; index++)
                {
                    HlsVideoProfile profile = profiles[index];
                    HlsVideoRendition rendition = HlsVideoRendition.Create(current.Format, stream,
                        options.SeparateAudio ? null : audio?.GetStream(index), allocator, profile.MaximumWidth,
                        profile.EncoderOptions, options.TargetDuration, output);
                    video.Add(rendition);
                    if (!dimensions.Add((rendition.Format.Width, rendition.Format.Height)))
                    {
                        throw new ArgumentException("The selected width bounds produce duplicate encoded dimensions.", nameof(options));
                    }
                }
                IContainerWriter[] destinations = options.SeparateAudio
                    ? audioOutputs.Select(rendition => (IContainerWriter)rendition.Writer).ToArray()
                    : video.Select(rendition => (IContainerWriter)rendition.Writer).ToArray();
                do
                {
                    using (VideoFrame frame = current)
                    {
                        current = null;
                        cancellationToken.ThrowIfCancellationRequested();
                        if (audio is not null) { await audio.WriteBeforeAsync(frame.Timestamp, destinations, cancellationToken).ConfigureAwait(false); }
                        foreach (HlsVideoRendition rendition in video)
                        {
                            await rendition.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                        }
                        count = checked(count + 1);
                        processed.Add(frame.Duration);
                        progress?.Invoke(new(count, processed.Duration, BytesWritten(), false));
                    }
                    if (await input.MoveNextAsync().ConfigureAwait(false)) { current = input.Current; }
                } while (current is not null);
                if (audio is not null) { await audio.WriteBeforeAsync(null, destinations, cancellationToken).ConfigureAwait(false); }
                foreach (HlsVideoRendition rendition in video) { await rendition.CompleteAsync(cancellationToken).ConfigureAwait(false); }
                foreach (HlsAudioOutput rendition in audioOutputs) { await rendition.Writer.CompleteAsync(cancellationToken).ConfigureAwait(false); }
                HlsPackagePlaylists.ValidateAlignment(video);
                await HlsPackagePlaylists.WriteAsync(output, video, audioOutputs, audioProfiles, cancellationToken).ConfigureAwait(false);
                long bytes = BytesWritten();
                progress?.Invoke(new(count, processed.Duration, bytes, true));
                return new(count, video.Count, video[0].Writer.Segments.Count, audio?.PacketsWritten ?? 0, bytes);
            }
            finally
            {
                current?.Dispose();
                await Task.WhenAll(video.Select(rendition => rendition.DisposeAsync().AsTask())
                    .Concat(audioOutputs.Select(rendition => rendition.DisposeAsync().AsTask()))).ConfigureAwait(false);
            }

            long BytesWritten() => checked(video.Sum(rendition => rendition.Writer.BytesWritten) + audioOutputs.Sum(rendition => rendition.Writer.BytesWritten));
        }

        private static void Validate(EncodedStreamInfo stream, HlsRenditionOptions options, HlsVideoProfile[] profiles,
            int[] audioProfiles, HlsAudioSource? audio)
        {
            if (stream.DisplayMatrix != VideoDisplayMatrix.Identity) { throw new NotSupportedException("Video encoding currently requires an identity display transform."); }
            options.TargetDuration.TimeBase.Validate();
            if (options.TargetDuration.Value <= 0 || profiles.Length == 0
                || profiles.Any(profile => profile is null || profile.MaximumWidth < 2 || profile.EncoderOptions is null))
            { throw new ArgumentException("Supply positive segment duration and at least one valid video profile.", nameof(options)); }
            if (options.SeparateAudio)
            {
                if (audio is null || audioProfiles.Length != profiles.Length || audioProfiles.Any(index => index < 0 || index >= audio.ProfileCount))
                { throw new ArgumentException("Separate audio requires a valid audio profile for each video variant.", nameof(options)); }
                audio.ValidateDestinationCount(audio.ProfileCount);
            }
            else
            {
                if (options.AudioProfileIndices is not null) { throw new ArgumentException("Audio profile selection requires separate audio.", nameof(options)); }
                audio?.ValidateDestinationCount(profiles.Length);
            }
        }

        private static async IAsyncEnumerable<VideoFrame> ReadAsync(VideoSession session, [EnumeratorCancellation] CancellationToken token)
        {
            while (await session.ReadFrameAsync(token).ConfigureAwait(false) is VideoFrame frame) { yield return frame; }
        }
    }
}
