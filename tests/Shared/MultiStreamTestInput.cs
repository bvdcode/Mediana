// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using Mediana.Codecs.Aac;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Execution;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class MultiStreamTestInput
    {
        public static async Task VerifyAsync(string path)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory()).AddAudioDecoder(new AacDecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
            using IncrementalHash video = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using IncrementalHash audio = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int expectedVideo = 0;
            int expectedAudio = 0;
            await using (VideoSession session = await engine.OpenVideoAsync(source))
            {
                while (await session.ReadFrameAsync() is VideoFrame frame)
                {
                    using (frame)
                    {
                        AppendVideo(video, frame);
                        expectedVideo++;
                    }
                }
            }
            await using (AudioSession session = await engine.OpenAudioAsync(source))
            {
                while (await session.ReadFrameAsync() is AudioFrame frame)
                {
                    using (frame)
                    {
                        AppendAudio(audio, frame);
                        expectedAudio++;
                    }
                }
            }
            byte[] expectedVideoHash = video.GetHashAndReset();
            byte[] expectedAudioHash = audio.GetHashAndReset();
            int actualVideo = 0;
            int actualAudio = 0;
            await using (MediaSession session = await engine.OpenMediaAsync(source))
            {
                while (await session.ReadFrameAsync() is DecodedMediaFrame frame)
                {
                    using (frame)
                    {
                        switch (frame.Kind)
                        {
                            case MediaKind.Video:
                                AppendVideo(video, frame.Video!);
                                actualVideo++;
                                break;
                            case MediaKind.Audio:
                                AppendAudio(audio, frame.Audio!);
                                actualAudio++;
                                break;
                            default:
                                throw new InvalidDataException("Unexpected decoded stream kind.");
                        }
                    }
                }
                await session.DisposeAsync();
                if (session.AllocatedBufferBytes != 0)
                {
                    throw new InvalidDataException("Multi-stream decoding retained buffers after disposal.");
                }
            }
            if (expectedVideo == 0 || expectedAudio == 0 || expectedVideo != actualVideo || expectedAudio != actualAudio ||
                !expectedVideoHash.AsSpan().SequenceEqual(video.GetHashAndReset()) ||
                !expectedAudioHash.AsSpan().SequenceEqual(audio.GetHashAndReset()))
            {
                throw new InvalidDataException("Joint demux output differs from independent video/audio decoding.");
            }
        }

        private static void AppendVideo(IncrementalHash hash, VideoFrame frame)
        {
            hash.AppendData(frame.Data.Span);
            AppendTimestamp(hash, frame.Timestamp);
        }

        private static void AppendAudio(IncrementalHash hash, AudioFrame frame)
        {
            for (int plane = 0; plane < frame.Format.PlaneCount; plane++)
            {
                hash.AppendData(frame.GetPlane(plane).Span);
            }
            AppendTimestamp(hash, frame.Timestamp);
        }

        private static void AppendTimestamp(IncrementalHash hash, MediaTimestamp? timestamp)
        {
            if (timestamp is MediaTimestamp value)
            {
                hash.AppendData(BitConverter.GetBytes(value.Value));
                hash.AppendData(BitConverter.GetBytes(value.TimeBase.Numerator));
                hash.AppendData(BitConverter.GetBytes(value.TimeBase.Denominator));
            }
        }
    }
}
