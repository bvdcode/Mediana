// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Codecs.Bmp;
using Mediana.Codecs.Flac;
using Mediana.Codecs.Gif;
using Mediana.Codecs.H264;
using Mediana.Codecs.Jbig2;
using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Jpeg2000;
using Mediana.Codecs.Mp3;
using Mediana.Codecs.Opus;
using Mediana.Codecs.Pcm;
using Mediana.Codecs.Png;
using Mediana.Codecs.Tiff;
using Mediana.Codecs.Vorbis;
using Mediana.Codecs.Webp;
using Mediana.Containers.Aiff;
using Mediana.Containers.Flac;
using Mediana.Containers.Matroska;
using Mediana.Containers.Mp3;
using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Containers.Ogg;
using Mediana.Containers.Wave;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Documents.Pdf;
using Mediana.Documents.OpenXml;
using Mediana.Documents.Presentation;
using Mediana.Documents.Word;
using Mediana.Execution;
using Mediana.Images.Heif;
using Mediana.Images.Svg;
using Mediana.Images.Tgs;
using Mediana.IO;
using Mediana.Processing;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    internal static class MediaExportTestInput
    {
        internal static MediaEngine CreateEngine() => new MediaEngineBuilder()
            .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            .AddContainer(new MatroskaReaderFactory()).AddContainer(new WaveReaderFactory()).AddContainer(new AiffReaderFactory())
            .AddContainer(new Mp3ReaderFactory()).AddContainer(new FlacReaderFactory()).AddContainer(new OggReaderFactory())
            .AddAudioDecoder(new PcmDecoderFactory()).AddAudioDecoder(new AiffPcmDecoderFactory())
            .AddAudioDecoder(new Mp3DecoderFactory()).AddAudioDecoder(new FlacDecoderFactory()).AddAudioDecoder(new AacDecoderFactory())
            .AddAudioDecoder(new OpusDecoderFactory()).AddAudioDecoder(new VorbisDecoderFactory())
            .AddVideoDecoder(new H264DecoderFactory()).AddVideoEncoder(new H264EncoderFactory())
            .AddAudioEncoder(new AacEncoderFactory()).AddContainerWriter(new MpegTsWriterFactory())
            .AddImageDecoder(new BmpImageDecoder()).AddImageDecoder(new IcoImageDecoder())
            .AddImageDecoder(new GifImageDecoder()).AddImageDecoder(new TiffImageDecoder()).AddImageDecoder(new WebpImageDecoder())
            .AddImageDecoder(new PngImageDecoder()).AddImageDecoder(new JpegImageDecoder())
            .AddImageDecoder(new HeicImageDecoder(SampleAspectRatio.Square)).AddImageDecoder(new Jpeg2000ImageDecoder(SampleAspectRatio.Square))
            .AddImageDecoder(new Jbig2ImageDecoder(SampleAspectRatio.Square)).AddImageDecoder(new SvgImageDecoder())
            .AddImageDecoder(new TgsImageDecoder()).AddImageDecoder(new PdfImageDecoder())
            .AddImageDecoder(new OpenXmlImageDecoder([new WordPageDecoder(), new PresentationPageDecoder()])).Build();

        internal static async Task VerifyVisualAsync(string root, string group, string file)
        {
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, group, file)));
            await VerifyVisualAsync(source, new());
        }

        internal static async Task VerifyDocumentAsync(bool presentation)
        {
            byte[] data = presentation ? PresentationTestData.Create() : WordTestInput.Create(
                "<w:p><w:r><w:t>First</w:t><w:br w:type=\"page\"/><w:t>Second</w:t></w:r></w:p>",
                WordRenderTestInput.Fonts, page: WordRenderTestInput.SmallPage);
            await using RandomAccessStreamByteSource source = new(new MemoryStream(data, writable: false));
            await VerifyVisualAsync(source, new() { PageIndex = 1 });
        }

        private static async Task VerifyVisualAsync(IByteSource source, ImageDecodeRequest request)
        {
            MediaEngine engine = CreateEngine();
            MediaBufferAllocator allocator = new(64 * 1024 * 1024);
            using MemoryStream encoded = new();
            VisualExportResult result = await engine.ExportPreviewAsync(source, encoded, new PngImageEncoder(), allocator,
                new() { Input = new() { Request = request }, ColorResolver = SdrPreviewColorPolicy.Resolve });
            Require(result.Frames == 1 && result.BytesWritten == encoded.Length && allocator.AllocatedBytes == 0);
            using (VideoFrame decoded = await engine.DecodeImageAsync(source, allocator, request))
            using (VideoFrame expected = SrgbConverter.ApplyWithColorPolicy(decoded, SdrPreviewColorPolicy.Resolve, allocator))
            using (VideoFrame actual = await new PngImageDecoder().DecodeAsync(encoded.ToArray(), new(), allocator))
            {
                Require(expected.Format.Width == result.Width && expected.Format.Height == result.Height);
                Require(expected.Data.Span.SequenceEqual(actual.Data.Span));
            }
            Require(allocator.AllocatedBytes == 0 && encoded.CanWrite);
        }

        internal static async Task VerifyPcmAsync(string root, string group, string file)
        {
            MediaEngine engine = CreateEngine();
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, group, file)));
            using MemoryStream expected = new();
            long samples = 0;
            await using (AudioSession reference = await engine.OpenAudioAsync(source))
            {
                while (await reference.ReadFrameAsync() is AudioFrame frame)
                {
                    using (frame)
                    using (AudioFrame pcm = AudioSampleConverter.ConvertOrRetain(frame, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, allocator))
                    {
                        await PcmStreamWriter.WriteLittleEndianAsync(expected, pcm, allocator);
                        samples += pcm.SamplesPerChannel;
                    }
                }
            }
            using MemoryStream output = new();
            List<MediaOutputProgress> progress = [];
            PcmExportResult result = await engine.ExportPcmAsync(source, output, new WavePcmWriterFactory(), allocator, progress: progress.Add);
            Require(result.SamplesPerChannel == samples && result.Format.SampleFormat == AudioSampleFormat.Signed16 &&
                allocator.AllocatedBytes == 0 && progress.Count > 0 && progress[^1].IsComplete);
            using MemoryStream decoded = new();
            output.Position = 0;
            await using RandomAccessStreamByteSource wave = new(output, leaveOpen: true);
            await using (AudioSession session = await engine.OpenAudioAsync(wave))
            { await PcmStreamWriter.WriteFramesAsync(session.ReadFrameAsync, decoded, allocator, PcmByteOrder.LittleEndian); }
            Require(expected.ToArray().AsSpan().SequenceEqual(decoded.ToArray()) && allocator.AllocatedBytes == 0 && output.CanWrite);
        }

        internal static async Task VerifyVideoEncodingAsync(string root)
        {
            MediaEngine engine = CreateEngine();
            MediaBufferAllocator allocator = new(8 * 1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, "Mp4", "avc-multislice-cabac.mp4")));
            using MemoryStream output = new();
            List<MediaOutputProgress> progress = [];
            MediaOperationProgress result = await engine.TranscodeVideoAsync(source, output, "h264", "mpegts", allocator,
                new() { MaximumWidth = 48, MaximumHeight = 40 }, progress.Add);
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "Mp4", "avc-multislice-cabac.scaled-48x40.ts"));
            Require(output.ToArray().AsSpan().SequenceEqual(expected) && result.Frames > 0 && allocator.AllocatedBytes == 0);
            Require(progress[^1].IsComplete && progress[^1].BytesWritten == output.Length && progress[^1].TotalDuration is not null);
        }

        internal static async Task VerifyAudioEncodingAsync(string root)
        {
            MediaEngine engine = CreateEngine();
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, "Wave", "s16-stereo.wav")));
            using MemoryStream output = new();
            AudioTranscodeResult result = await engine.TranscodeAudioAsync(source, output, "aac", "mpegts", allocator);
            Require(result.SamplesPerChannel > 0 && result.Encoding.IsComplete && result.PrimingSamples == 1024 && result.PaddingSamples is not null);
            Require(output.Length > 0 && output.Length % 188 == 0 && allocator.AllocatedBytes == 0 && output.CanWrite);
        }

        internal static async Task VerifyAnimationAsync(string root, string group, string file)
        {
            MediaEngine engine = CreateEngine();
            MediaBufferAllocator allocator = new(32 * 1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, group, file)));
            using MemoryStream output = new();
            VisualExportResult result = await engine.ExportAnimationAsync(source, output, new WebpImageSequenceEncoder(), allocator,
                new() { ColorResolver = SdrPreviewColorPolicy.Resolve });
            Require(result.Frames > 1 && result.BytesWritten == output.Length && result.ProcessedDuration is not null && allocator.AllocatedBytes == 0);
            int frames = 0;
            await foreach (VideoFrame frame in engine.DecodeImageFramesAsync(output.ToArray(), allocator))
            { using (frame) { frames++; Require(frame.Format.Width == result.Width && frame.Format.Height == result.Height); } }
            Require(frames == result.Frames && allocator.AllocatedBytes == 0);
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("Public media export differs in pixels, samples, timing, progress or buffer ownership."); }
        }
    }
}
