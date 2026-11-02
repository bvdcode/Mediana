// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using Mediana.Codecs.Aac;
using Mediana.Codecs.H264;
using Mediana.Codecs.Mp3;
using Mediana.Codecs.Png;
using Mediana.Containers.Matroska;
using Mediana.Containers.Mp3;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class ContainerDecodeTransferTestInput
    {
        internal static async Task VerifyAsync(string root)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddContainer(new MatroskaReaderFactory()).AddContainer(new Mp3ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory()).AddAudioDecoder(new AacDecoderFactory())
                .AddAudioDecoder(new Mp3DecoderFactory()).AddImageDecoder(new PngImageDecoder()).Build();
            foreach (string name in new[] { "Mp4/avc-aac.mp4", "Matroska/avc-aac-attachment.mkv", "Mp3/stereo-vbr-cover.mp3" })
            {
                byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, name));
                using MemoryStream input = new(encoded, writable: false);
                await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
                input.Position = 3;
                byte[] expected = await ReadAudioAsync(engine, source);
                await using ContainerSession container = await engine.OpenContainerAsync(source);
                using ContainerImage? picture = await engine.ReadContainerImageAsync(container,
                    new([ContainerImageSource.Attachment, ContainerImageSource.Picture]));
                EncodedStreamInfo stream = container.Streams.First(stream => stream.Kind == MediaKind.Audio);
                await using AudioSession audio = engine.OpenAudio(container, stream.StreamIndex);
                Require(ReferenceEquals(stream, audio.Stream));
                await container.DisposeAsync();
                byte[] actual = await HashAudioAsync(audio);
                Require(expected.AsSpan().SequenceEqual(actual));
                await audio.DisposeAsync();
                picture?.Dispose();
                Require(container.AllocatedBufferBytes == 0 && input.Position == 3 && input.CanRead);
                if (stream.CodecId == "aac")
                {
                    await VerifyVideoAsync(engine, root, encoded);
                }
            }
        }

        private static async Task<byte[]> ReadAudioAsync(MediaEngine engine, IByteSource source)
        {
            await using AudioSession session = await engine.OpenAudioAsync(source);
            return await HashAudioAsync(session);
        }

        private static async Task<byte[]> HashAudioAsync(AudioSession session)
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int frames = 0;
            while (await session.ReadFrameAsync() is AudioFrame frame)
            {
                using (frame)
                {
                    hash.AppendData(BitConverter.GetBytes(frame.SamplesPerChannel));
                    hash.AppendData(BitConverter.GetBytes(frame.Format.SampleRate));
                    hash.AppendData(BitConverter.GetBytes(frame.Format.Channels.Count));
                    MediaTimestamp timestamp = frame.Timestamp ?? throw new InvalidDataException("Missing reference audio timestamp.");
                    hash.AppendData(BitConverter.GetBytes(timestamp.Value));
                    hash.AppendData(BitConverter.GetBytes(timestamp.TimeBase.Numerator));
                    hash.AppendData(BitConverter.GetBytes(timestamp.TimeBase.Denominator));
                    for (int plane = 0; plane < frame.Format.PlaneCount; plane++)
                    {
                        hash.AppendData(frame.GetPlane(plane).Span);
                    }
                    frames++;
                }
            }
            Require(frames > 0);
            return hash.GetHashAndReset();
        }

        private static async Task VerifyVideoAsync(MediaEngine engine, string root, byte[] encoded)
        {
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "Mp4", "avc-aac.sequence.yuv"));
            await using EbmlElementTestSource source = new(encoded) { MaximumRead = 17 };
            await using ContainerSession container = await engine.OpenContainerAsync(source);
            using ContainerImage? picture = await engine.ReadContainerImageAsync(container,
                new([ContainerImageSource.Attachment, ContainerImageSource.Picture]));
            EncodedStreamInfo stream = container.Streams.First(stream => stream.Kind == MediaKind.Video);
            int calls = source.ReadCalls;
            long storage = container.AllocatedBufferBytes;
            await using VideoSession video = engine.OpenVideo(container);
            Require(source.ReadCalls == calls && video.AllocatedBufferBytes >= storage && ReferenceEquals(stream, video.Stream));
            await container.DisposeAsync();
            Require(video.CanSeek);
            int position = 0;
            int frames = 0;
            MediaTimestamp? previous = null;
            while (await video.ReadFrameAsync() is VideoFrame frame)
            {
                using (frame)
                {
                    Require(frame.Timestamp is not null && (previous is null || frame.Timestamp.Value.CompareTo(previous.Value) > 0));
                    previous = frame.Timestamp;
                    for (int plane = 0; plane < frame.Planes.Count; plane++)
                    {
                        for (int row = 0; row < frame.Planes[plane].Height; row++)
                        {
                            int count = frame.Planes[plane].RowBytes;
                            Require(position <= expected.Length - count && frame.GetRow(plane, row).SequenceEqual(expected.AsSpan(position, count)));
                            position += count;
                        }
                    }
                    frames++;
                }
            }
            Require(position == expected.Length && frames > 1);
            await video.SeekAsync(new(0, new(1, 1)));
            using VideoFrame retained = await video.ReadFrameAsync() ?? throw new InvalidDataException("Missing restarted video frame.");
            await video.DisposeAsync();
            Require(retained.GetRow(0, 0).SequenceEqual(expected.AsSpan(0, retained.Planes[0].RowBytes)) && !source.IsDisposed);
            retained.Dispose();
            picture?.Dispose();
            Require(container.AllocatedBufferBytes == 0);
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("Container transfer changed decoded data, timing or resource ownership."); }
        }
    }
}
