// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeMediaExportTests
    {
        internal static async Task RunAsync()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            foreach ((string group, string file) in new (string, string)[]
            {
                ("Png", "c6-d16-i1-13x11.png"),
                ("Raster", "bitmap-RGB.bmp"), ("Raster", "icon-png.ico"), ("Raster", "transparent.gif"),
                ("Raster", "tiff-RGBA-tiff_lzw.tif"), ("Raster", "webp-gradient-4.webp"),
                ("Jpeg", "rgb-s2-1x1.jpg"), ("Heif", "basic-8bit.heic"), ("Jpeg2000", "RGB-129x101.jp2"),
                ("Jbig2", "mmr-white.jb2"), ("Svg", "vector-preview.svg"), ("Tgs", "geometry.tgs"), ("Pdf", "graphics.pdf")
            }) { await MediaExportTestInput.VerifyVisualAsync(root, group, file); }
            await MediaExportTestInput.VerifyDocumentAsync(false);
            await MediaExportTestInput.VerifyDocumentAsync(true);
            foreach ((string group, string file) in new (string, string)[]
            {
                ("Wave", "f64-stereo.wav"), ("Aiff", "f64-stereo.aiff"), ("Mp3", "mono-speech.mp3"),
                ("Flac", "mono-speech.flac"), ("Mp4", "avc-aac.mp4"), ("Matroska", "avc-aac-attachment.mkv"),
                ("Ogg", "opus-stereo.ogg"), ("Ogg", "vorbis-stereo.ogg")
            }) { await MediaExportTestInput.VerifyPcmAsync(root, group, file); }
            await MediaExportTestInput.VerifyAnimationAsync(root, "Raster", "webp-animation.webp");
            await MediaExportTestInput.VerifyAnimationAsync(root, "Raster", "transparent.gif");
            await MediaExportTestInput.VerifyAnimationAsync(root, "Tgs", "geometry.tgs");
            await MediaExportTestInput.VerifyAnimationAsync(root, "Mp4", "avc-multislice-cabac.mp4");
            await MediaExportTestInput.VerifyVideoEncodingAsync(root);
            await MediaExportTestInput.VerifyAudioEncodingAsync(root);
            Console.WriteLine("PASS public media exports: fifteen visual families, eight audio sources, four animation sources, audio/video encoding, complete pixels/samples and released buffer ownership.");
        }
    }
}
