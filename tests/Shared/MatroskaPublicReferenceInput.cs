// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Codecs.H264;
using Mediana.Codecs.Mp3;
using Mediana.Containers.Matroska;
using Mediana.Containers.Mp4;
using Mediana.Core.Audio;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using static Mediana.Tests.MatroskaMetadataTestData;

namespace Mediana.Tests
{
    internal static class MatroskaPublicReferenceInput
    {
        public static async Task VerifyAsync(byte[] bytes, int audioStream, byte[] expectedAudio,
            IReadOnlyList<MediaTimestamp?> audioTimes, byte[] expectedVideo, MediaTimestamp[] videoTimes)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory())
                .AddAudioDecoder(new AacDecoderFactory()).AddAudioDecoder(new Mp3DecoderFactory())
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            MediaOpenOptions options = new() { BufferBudgetBytes = 2 * 1024 * 1024 };
            await using EbmlElementTestSource source = new(bytes) { MaximumRead = 7 };
            MediaInfo info = await engine.ProbeAsync(source, options);
            Require(info.Container!.FormatId == "matroska" && !source.IsDisposed);
            Require(info.Container.StreamInfo![audioStream].PacketIndex!.PacketCount == audioTimes.Count);
            Require(info.Container.StreamInfo[audioStream].PacketIndex!.DecodeDuration is null);
            int offset = 0;
            int position = 0;
            await using (AudioSession audio = await engine.OpenAudioAsync(source, audioStream,
                options with { MaxPacketReads = audioTimes.Count + 1 }))
            {
                while (await audio.ReadFrameAsync() is AudioFrame frame)
                {
                    using (frame)
                    {
                        Require(position < audioTimes.Count && frame.Timestamp == audioTimes[position++]);
                        ReadOnlyMemory<byte> plane = frame.GetPlane(0);
                        Require(plane.Span.SequenceEqual(expectedAudio.AsSpan(offset, plane.Length)));
                        offset += plane.Length;
                    }
                }
                Require(offset == expectedAudio.Length && position == audioTimes.Count);
                await audio.DisposeAsync();
                Require(audio.AllocatedBufferBytes == 0 && !source.IsDisposed);
            }
            if (expectedVideo.Length == 0)
            {
                return;
            }
            int frameBytes = expectedVideo.Length / videoTimes.Length;
            int picture = 0;
            await using VideoSession video = await engine.OpenVideoAsync(source, options);
            Require(video.CanSeek && video.Stream.DisplayMatrix == VideoDisplayMatrix.Identity);
            while (await video.ReadFrameAsync() is VideoFrame frame)
            {
                using (frame)
                {
                    Require(picture < videoTimes.Length && frame.Timestamp == videoTimes[picture]);
                    H264TestInput.VerifyFrame(frame, expectedVideo.AsSpan(picture * frameBytes, frameBytes));
                    picture++;
                }
            }
            Require(picture == videoTimes.Length);
            for (int selected = videoTimes.Length - 1; selected >= 0; selected--)
            {
                await video.SeekAsync(videoTimes[selected]);
                using VideoFrame selectedFrame = (await video.ReadFrameAtOrAfterAsync(videoTimes[selected]))!;
                Require(selectedFrame.Timestamp == videoTimes[selected]);
                H264TestInput.VerifyFrame(selectedFrame, expectedVideo.AsSpan(selected * frameBytes, frameBytes));
            }
            MediaTimestamp middle = new(650, new TimeBase(1, 1000));
            await video.SeekAsync(middle);
            using (VideoFrame selectedFrame = (await video.ReadFrameAtOrAfterAsync(middle))!)
            {
                Require(selectedFrame.Timestamp == videoTimes[4]);
                H264TestInput.VerifyFrame(selectedFrame, expectedVideo.AsSpan(4 * frameBytes, frameBytes));
                MediaEngine originalEngine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory(
                    new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                    .AddVideoDecoder(new H264DecoderFactory()).Build();
                byte[] originalBytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4", "avc-aac.mp4"));
                await using EbmlElementTestSource originalSource = new(originalBytes);
                await using VideoSession originalSession = await originalEngine.OpenVideoAsync(originalSource);
                await originalSession.SeekAsync(middle);
                using VideoFrame original = (await originalSession.ReadFrameAtOrAfterAsync(middle))!;
                H264TestInput.VerifyFrame(original, expectedVideo.AsSpan(4 * frameBytes, frameBytes));
                Require(selectedFrame.Format.GetColorInterpretation() == original.Format.GetColorInterpretation());
                Require(selectedFrame.Format.SampleAspectRatio == original.Format.SampleAspectRatio);
                Require(video.Stream.DisplayMatrix == originalSession.Stream.DisplayMatrix);
            }
            await video.SeekAsync(videoTimes[3]);
            using VideoFrame retained = (await video.ReadFrameAtOrAfterAsync(videoTimes[3]))!;
            Require(retained.Timestamp == videoTimes[3]);
            await video.DisposeAsync();
            H264TestInput.VerifyFrame(retained, expectedVideo.AsSpan(3 * frameBytes, frameBytes));
            retained.Dispose();
            Require(video.AllocatedBufferBytes == 0 && !source.IsDisposed);
        }
    }
}
