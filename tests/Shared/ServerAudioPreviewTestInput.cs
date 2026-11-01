// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Codecs.Flac;
using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Mp3;
using Mediana.Codecs.Pcm;
using Mediana.Codecs.Png;
using Mediana.Containers;
using Mediana.Containers.Flac;
using Mediana.Containers.Matroska;
using Mediana.Containers.Mp3;
using Mediana.Containers.Mp4;
using Mediana.Containers.Wave;
using Mediana.IO;
using Mediana.Sample;

namespace Mediana.Tests
{
    internal static class ServerAudioPreviewTestInput
    {
        internal static async Task VerifyCoversAsync(string root)
        {
            (string Name, IContainerReaderFactory Factory)[] cases =
            [
                ("Mp3/stereo-vbr-cover.mp3", new Mp3ReaderFactory()),
                ("Flac/stereo-covers.flac", new FlacReaderFactory()),
                ("Metadata/covers.m4a", new Mp4ReaderFactory()),
                ("Matroska/avc-aac-attachment.mkv", new MatroskaReaderFactory())
            ];
            byte[] pixels = await File.ReadAllBytesAsync(Path.Combine(root, "Png", "cover.rgba"));
            foreach ((string name, IContainerReaderFactory implementation) in cases)
            {
                string directory = Directory.CreateTempSubdirectory("mediana-audio-preview-").FullName;
                try
                {
                    using MemoryStream input = new(await File.ReadAllBytesAsync(Path.Combine(root, name)), writable: false);
                    await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
                    input.Position = 3;
                    ObservedContainerFactory factory = new(implementation);
                    MediaEngine engine = new MediaEngineBuilder().AddContainer(factory)
                        .AddImageDecoder(new PngImageDecoder()).Build();
                    string image = Path.Combine(directory, "cover.png");
                    string waveform = Path.Combine(directory, "waveform.json");
                    AudioPreviewKind kind = await AudioPreviewSample.WriteAsync(engine, source, image, waveform);
                    Require(kind == AudioPreviewKind.Cover && factory.Opens == 1 && !File.Exists(waveform));
                    PngTestInput.VerifyPreview(await File.ReadAllBytesAsync(image), 24, 16, pixels, 24, static (x, y) => (x, y), 0);
                    Require(factory.Allocator!.AllocatedBytes == 0 && input.Position == 3 && input.CanRead);
                }
                finally
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        internal static async Task VerifyWaveformsAsync(string root)
        {
            (string Name, IContainerReaderFactory Factory)[] cases =
            [
                ("Waveform/speech.wav", new WaveReaderFactory()),
                ("Waveform/fractional.wav", new WaveReaderFactory()),
                ("Mp3/mono-speech.mp3", new Mp3ReaderFactory()),
                ("Flac/mono-speech.flac", new FlacReaderFactory()),
                ("Mp4/avc-aac.mp4", new Mp4ReaderFactory())
            ];
            foreach ((string name, IContainerReaderFactory implementation) in cases)
            {
                string directory = Directory.CreateTempSubdirectory("mediana-audio-waveform-").FullName;
                try
                {
                    using MemoryStream input = new(await File.ReadAllBytesAsync(Path.Combine(root, name)), writable: false);
                    await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
                    input.Position = 3;
                    using MemoryStream expected = new();
                    await AudioWaveformSample.WriteAsync(AudioWaveformSample.CreateEngine(), source, expected);
                    ObservedContainerFactory factory = new(implementation);
                    MediaEngine engine = new MediaEngineBuilder().AddContainer(factory)
                        .AddImageDecoder(new PngImageDecoder()).AddImageDecoder(new JpegImageDecoder())
                        .AddAudioDecoder(new PcmDecoderFactory()).AddAudioDecoder(new Mp3DecoderFactory())
                        .AddAudioDecoder(new FlacDecoderFactory()).AddAudioDecoder(new AacDecoderFactory()).Build();
                    string image = Path.Combine(directory, "cover.png");
                    string waveform = Path.Combine(directory, "waveform.json");
                    AudioPreviewKind kind = await AudioPreviewSample.WriteAsync(engine, source, image, waveform);
                    Require(kind == AudioPreviewKind.Waveform && factory.Opens == 1 && !File.Exists(image));
                    Require((await File.ReadAllBytesAsync(waveform)).AsSpan().SequenceEqual(expected.ToArray()));
                    Require(factory.Allocator!.AllocatedBytes == 0 && input.Position == 3 && input.CanRead);
                }
                finally
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        internal static async Task VerifyRecognizedFailureAsync(string root, bool cancel)
        {
            byte[] png = await File.ReadAllBytesAsync(Path.Combine(root, "Metadata", "cover.png"));
            byte[] payload = cancel ? png : png[..16];
            ContainerImageTestFactory implementation = new([new(payload, payload.Length)], [new(png, png.Length)]);
            using CancellationTokenSource cancellation = new();
            ObservedContainerFactory factory = new(implementation)
            {
                Opened = _ =>
                {
                    if (cancel)
                    {
                        implementation.Reader!.AfterPayload = () => new ValueTask(cancellation.CancelAsync());
                    }
                }
            };
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).AddImageDecoder(new PngImageDecoder()).Build();
            await using EbmlElementTestSource source = new("IMGS"u8.ToArray());
            string directory = Directory.CreateTempSubdirectory("mediana-audio-failure-").FullName;
            bool rejected = false;
            try
            {
                try
                {
                    await AudioPreviewSample.WriteAsync(engine, source, Path.Combine(directory, "cover.png"),
                        Path.Combine(directory, "waveform.json"), cancellation.Token);
                }
                catch (InvalidDataException) when (!cancel) { rejected = true; }
                catch (OperationCanceledException) when (cancel) { rejected = true; }
                Require(rejected && !source.IsDisposed && factory.Opens == 1 && factory.Allocator!.AllocatedBytes == 0);
                Require(implementation.Reader!.PayloadReads.SequenceEqual(new[] { (ContainerImageSource.Picture, 0) }));
                Require(implementation.Reader.PacketReads == 0 && Directory.GetFileSystemEntries(directory).Length == 0);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("Audio preview changed image selection, waveform data, source reads or storage ownership.");
            }
        }
    }
}
