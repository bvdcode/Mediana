// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Execution;
using Mediana.IO;
using Mediana.Processing;
using Mediana.Processing.Audio;

namespace Mediana.Sample
{
    internal static class AudiovisualWindowSample
    {
        public static async Task<int> RunAsync(string input, string output, long startMilliseconds, long endMilliseconds)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(startMilliseconds);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(endMilliseconds, startMilliseconds);
            MediaTimestamp start = new(startMilliseconds, new(1, 1000));
            MediaTimestamp end = new(endMilliseconds, new(1, 1000));
            MediaBufferAllocator allocator = new(128 * 1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            Mp4TrackInfo audioTrack;
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                audioTrack = reader.Tracks.First(track => track.Stream.Kind == MediaKind.Audio);
            }
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddVideoDecoder(new H264DecoderFactory())
                .AddAudioDecoder(new AacDecoderFactory()).Build();
            await using VideoSession video = await engine.OpenVideoAsync(source);
            await using AudioSession audio = await engine.OpenAudioAsync(source, audioTrack.Stream.StreamIndex);
            if (video.Stream.DisplayMatrix != VideoDisplayMatrix.Identity)
            {
                throw new NotSupportedException("This sample requires an identity video display transform.");
            }
            await video.SeekAsync(start);
            await using VideoTrimmer pictures = new(video.ReadFrameAsync, start, end);
            using VideoFrame firstPicture = await pictures.ReadFrameAsync()
                ?? throw new InvalidDataException("The requested interval contains no video pictures.");
            await audio.SeekAsync(start);
            AudioFrame? original = await audio.ReadFrameAsync()
                ?? throw new InvalidDataException("The audio track contains no PCM samples.");
            try
            {
                Mp4AudioSampleRange? edit = audioTrack.Edits.Count == 0 ? null
                    : Mp4AudioSampleRange.FromEditList(audioTrack, original.Format.SampleRate);
                MediaTimestamp origin = original.Timestamp
                    ?? throw new InvalidDataException("The audio track contains PCM without presentation timestamps.");
                await using AudioTrimmer edited = AudioTrimmer.CreateTimeWindow(ReadOriginalAsync,
                    edit?.PresentationTime ?? origin, edit?.GetPresentationEnd(original.Format.SampleRate), allocator);
                await using AudioTrimmer samples = AudioTrimmer.CreateTimeWindow(edited.ReadFrameAsync, start, end, allocator);
                using AudioFrame firstSamples = await samples.ReadFrameAsync()
                    ?? throw new InvalidDataException("The requested interval contains no audio samples.");
                AudioChannelMixer downmix = AudioDownmix.CreateStereo(firstSamples.Format, AudioSampleFormat.Float32);
                using AudioFrame prepared = downmix.Apply(firstSamples, allocator);
                using H264Encoder videoEncoder = H264Encoder.Create(firstPicture.Format, video.Stream.TimeBase, allocator, streamIndex: 0);
                using AacEncoder audioEncoder = AacEncoder.Create(prepared.Format, prepared.Timestamp!.Value.TimeBase, allocator, streamIndex: 1);
                bool openingVideo = true;
                bool openingAudio = true;
                long pictureCount = 0;
                long sampleCount = 0;
                await using MediaEncodeSource<VideoFrame> encodedVideo = new(ReadVideoAsync, videoEncoder);
                await using MediaEncodeSource<AudioFrame> encodedAudio = new(ReadAudioAsync, audioEncoder);
                await using (FileStream destination = new(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await using MpegTsWriter writer = MpegTsWriter.Create(destination,
                        [videoEncoder.Stream, audioEncoder.Stream], allocator);
                    long packets = await MediaPacketMerge.WriteAsync([encodedVideo, encodedAudio], writer);
                    await Console.Out.WriteLineAsync($"Encoded {pictureCount} pictures, {sampleCount} audio samples/channel and {packets} packets. AAC priming: {audioEncoder.PrimingSamples}; padding: {audioEncoder.PaddingSamples}. TS timestamps: source movie time + 1 second.");
                }

                async ValueTask<VideoFrame?> ReadVideoAsync(CancellationToken token)
                {
                    VideoFrame? frame = openingVideo ? firstPicture.Retain() : await pictures.ReadFrameAsync(token);
                    if (openingVideo)
                    {
                        openingVideo = false;
                        firstPicture.Dispose();
                    }
                    if (frame is not null)
                    {
                        pictureCount++;
                    }
                    return frame;
                }

                async ValueTask<AudioFrame?> ReadAudioAsync(CancellationToken token)
                {
                    AudioFrame? result;
                    if (openingAudio)
                    {
                        openingAudio = false;
                        result = prepared.Retain();
                        prepared.Dispose();
                        firstSamples.Dispose();
                    }
                    else
                    {
                        using AudioFrame? frame = await samples.ReadFrameAsync(token);
                        result = frame is null ? null : downmix.Apply(frame, allocator, token);
                    }
                    sampleCount = checked(sampleCount + (result?.SamplesPerChannel ?? 0));
                    return result;
                }
            }
            finally
            {
                original?.Dispose();
            }
            firstPicture.Dispose();
            await video.DisposeAsync();
            await audio.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || video.AllocatedBufferBytes != 0 || audio.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("Audiovisual interval buffers were not released.");
            }
            await Console.Out.WriteLineAsync("Live buffer bytes: 0.");
            return 0;

            ValueTask<AudioFrame?> ReadOriginalAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (original is not null)
                {
                    AudioFrame frame = original;
                    original = null;
                    return ValueTask.FromResult<AudioFrame?>(frame);
                }
                return audio.ReadFrameAsync(token);
            }
        }
    }
}
