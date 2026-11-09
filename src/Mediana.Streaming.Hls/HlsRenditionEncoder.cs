// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Execution;
using Mediana.Processing;

namespace Mediana.Streaming.Hls
{
    /// <summary>Encodes an aligned H.264 rendition ladder, coordinates audio delivery and writes a complete VOD package.
    /// Clips overlapping display durations with one-frame lookahead before encoding.
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
            await using VideoPresentationReader presentation = new(ReadFrameAsync);
            VideoFrame? current = await presentation.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            if (current is null) { throw new InvalidDataException("The input contains no decoded video frames."); }
            try
            {
                using VideoPreparationSequence preparation = new(current.Format, stream.DisplayMatrix!.Value, allocator, options.FramePreparation);
                if (options.SeparateAudio)
                {
                    for (int profile = 0; profile < audio!.ProfileCount; profile++)
                    {
                        audioOutputs.Add(HlsAudioOutput.Create(audio.GetStream(profile), allocator, options.TargetDuration, output, profile, options.Bandwidth));
                    }
                }
                HashSet<(int Width, int Height)> dimensions = [];
                for (int index = 0; index < profiles.Length; index++)
                {
                    HlsVideoProfile profile = profiles[index];
                    HlsVideoRendition rendition = HlsVideoRendition.Create(preparation.OutputFormat, stream,
                        options.SeparateAudio ? null : audio?.GetStream(index), allocator, profile.MaximumWidth,
                        profile.EncoderOptions, options.TargetDuration, output, options.Bandwidth);
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
                        using VideoFrame prepared = preparation.Apply(frame, cancellationToken);
                        if (audio is not null) { await audio.WriteBeforeAsync(frame.Timestamp, destinations, cancellationToken).ConfigureAwait(false); }
                        foreach (HlsVideoRendition rendition in video)
                        {
                            await rendition.WriteAsync(prepared, cancellationToken).ConfigureAwait(false);
                        }
                        count = checked(count + 1);
                        processed.Add(frame.Duration);
                        progress?.Invoke(new(count, processed.Duration, BytesWritten(), false));
                    }
                    current = await presentation.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
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

            async ValueTask<VideoFrame?> ReadFrameAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (!await input.MoveNextAsync().ConfigureAwait(false)) { return null; }
                return input.Current;
            }
        }

        private static void Validate(EncodedStreamInfo stream, HlsRenditionOptions options, HlsVideoProfile[] profiles,
            int[] audioProfiles, HlsAudioSource? audio)
        {
            if (stream.DisplayMatrix is null) { throw new NotSupportedException("Video encoding requires a known source display transform."); }
            ArgumentNullException.ThrowIfNull(options.Bandwidth);
            options.Bandwidth.Validate();
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
