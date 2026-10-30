// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Aiff;
using Mediana.Containers.Flac;
using Mediana.Containers.Mp3;
using Mediana.Containers.Mp4;
using Mediana.Containers.Wave;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class MediaProbeTestInput
    {
        public static MediaEngine CreateEngine() => new MediaEngineBuilder()
            .AddContainer(new Mp4ReaderFactory())
            .AddContainer(new Mp3ReaderFactory())
            .AddContainer(new FlacReaderFactory())
            .AddContainer(new WaveReaderFactory())
            .AddContainer(new AiffReaderFactory()).Build();

        public static readonly (string Folder, string File, string Format, long Value, int Denominator, string[] Codecs)[] Cases =
        [
            ("Wave", "s16-stereo.wav", "wave", 6058, 44100, ["pcm"]),
            ("Aiff", "s16-stereo.aiff", "aiff", 6058, 44100, ["pcm.aiff"]),
            ("Flac", "stereo24.flac", "flac", 9973, 44117, ["flac"]),
            ("Flac", "mono-speech.flac", "flac", 54400, 16000, ["flac"]),
            ("Mp3", "stereo-vbr-cover.mp3", "mp3", 254592, 44100, ["mp3"]),
            ("Mp4", "avc-aac.mp4", "mp4", 8, 5, ["h264", "aac"]),
            ("Mp4", "avc-tail.mov", "mp4", 6, 5, ["h264"]),
            ("Mp4", "avc-mp3-22.mp4", "mp4", 36, 5, ["h264", "mp3"])
        ];

        public static async Task<MediaInfo> VerifyAsync(string root, int index)
        {
            (string folder, string file, string format, long value, int denominator, string[] codecs) = Cases[index];
            MediaEngine engine = CreateEngine();
            if (engine.AudioDecoderImplementations.Count != 0 || engine.VideoDecoderImplementations.Count != 0)
            {
                throw new InvalidOperationException("Metadata probing must not require a registered decoder.");
            }
            MediaInfo info;
            await using (RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, folder, file))))
            {
                info = await engine.ProbeAsync(source);
            }
            if (info.Container.FormatId != format || !info.Streams.Select(stream => stream.CodecId).SequenceEqual(codecs))
            {
                throw new InvalidDataException($"Container or stream metadata differs for {file}.");
            }
            MediaDuration duration = info.Container.DeclaredDuration
                ?? throw new InvalidDataException($"The declared duration is missing for {file}.");
            if ((Int128)duration.Value * duration.TimeBase.Numerator * denominator != (Int128)value * duration.TimeBase.Denominator)
            {
                throw new InvalidDataException($"The declared duration differs for {file}.");
            }
            return info;
        }
    }
}
