// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Core.Video;

namespace Mediana.Cli
{
    internal static class CliApplication
    {
        public static async Task<int> RunAsync(string[] args)
        {
            if (args.Length == 5 && args[0] == "--pdf-png" &&
                int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int pdfPage) && pdfPage > 0 &&
                int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out int pdfWidth) && pdfWidth > 0)
            {
                return await PdfPreviewCommand.RenderAsync(args[1], args[2], pdfPage, pdfWidth);
            }
            if (args.Length == 5 && args[0] == "--pdf-image-png" &&
                int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int imagePage) && imagePage > 0 &&
                int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out int pdfImage) && pdfImage >= 0)
            {
                return await PdfPreviewCommand.ImageAsync(args[1], args[2], imagePage, pdfImage);
            }
            if (args.Length == 6 && args[0] == "--image-png" &&
                int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out int aspectWidth) && aspectWidth > 0 &&
                int.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture, out int aspectHeight) && aspectHeight > 0)
            {
                return await ImagePreviewCommand.RasterAsync(args[1], args[2], args[3], new SampleAspectRatio(aspectWidth, aspectHeight));
            }
            if (args.Length == 4 && args[0] == "--svg-png" &&
                int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int svgWidth) && svgWidth > 0)
            {
                return await ImagePreviewCommand.SvgAsync(args[1], args[2], svgWidth);
            }
            if (args.Length == 5 && args[0] == "--tgs-png" &&
                double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && double.IsFinite(seconds) && seconds >= 0 &&
                int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out int tgsWidth) && tgsWidth > 0)
            {
                return await ImagePreviewCommand.TgsAsync(args[1], args[2], seconds, tgsWidth);
            }

            if (args.Length == 2 && args[0] == "--probe-mp3")
            {
                return await Mp3ProbeCommand.RunAsync(args[1]);
            }
            if (args.Length == 4 && args[0] == "--cover-mp3" &&
                int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int mp3PictureIndex))
            {
                return await Mp3PictureCommand.RunAsync(args[1], args[2], mp3PictureIndex);
            }
            if (args.Length == 2 && args[0] == "--probe-flac")
            {
                return await FlacProbeCommand.RunAsync(args[1]);
            }
            if (args.Length == 4 && args[0] == "--cover-flac" &&
                int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int pictureIndex))
            {
                return await FlacPictureCommand.RunAsync(args[1], args[2], pictureIndex);
            }
            if (args.Length == 7 && args[0] == "--waveform-audio" &&
                int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int waveformStreamIndex) &&
                int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out int waveformRate) && waveformRate > 0 &&
                int.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture, out int samplesPerWindow) && samplesPerWindow > 0)
            {
                double[] gains = args[6].Split(',').Select(value => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
                return await AudioWaveformCommand.RunAsync(args[1], args[2], waveformStreamIndex, waveformRate, samplesPerWindow, gains);
            }
            if (args.Length == 4 && args[0] == "--cover-mp4" &&
                int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int coverIndex))
            {
                return await Mp4CoverArtCommand.RunAsync(args[1], args[2], coverIndex);
            }
            if (args.Length == 6 && args[0] == "--resample-audio" &&
                int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int resampleStreamIndex) &&
                int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out int sampleRate) && sampleRate > 0)
            {
                double[] gains = args[5].Split(',').Select(value => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
                return await AudioResampleCommand.RunAsync(args[1], args[2], resampleStreamIndex, sampleRate, gains);
            }
            if (args.Length == 4 && args[0] == "--decode-audio" &&
                int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int audioStreamIndex))
            {
                return await AudioDecodeCommand.RunAsync(args[1], args[2], audioStreamIndex);
            }
            if (args.Length == 7 && args[0] == "--preview" &&
                int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int streamIndex) &&
                long.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out long milliseconds) &&
                int.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture, out int maximumWidth) && maximumWidth > 0 &&
                int.TryParse(args[6], NumberStyles.None, CultureInfo.InvariantCulture, out int maximumHeight) && maximumHeight > 0)
            {
                return await VideoPreviewCommand.RunAsync(args[1], args[2], streamIndex, milliseconds, maximumWidth, maximumHeight);
            }
            if (args.Length == 2 && args[0] == "--probe-mp4")
            {
                return await Mp4ProbeCommand.RunAsync(args[1]);
            }
            if (args.Length == 3 && args[0] == "--remux-mp4")
            {
                return await Mp4RemuxCommand.RunAsync(args[1], args[2]);
            }
            if (args.Length == 4 && args[0] == "--plan-hls-mp4" &&
                int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int videoIndex) &&
                long.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out long targetMilliseconds) && targetMilliseconds > 0)
            {
                return await HlsPlanCommand.RunAsync(args[1], videoIndex, targetMilliseconds);
            }
            if (args.Length == 6 && args[0] == "--hls-mp4" &&
                int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int hlsVideoIndex) &&
                TryAudioIndex(args[4], out int? hlsAudioIndex) &&
                long.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture, out long hlsTargetMilliseconds) && hlsTargetMilliseconds > 0)
            {
                return await HlsCopyCommand.RunAsync(args[1], args[2], hlsVideoIndex, hlsAudioIndex, hlsTargetMilliseconds);
            }
            if (args.Length == 0 || (args.Length == 1 && args[0] == "--help"))
            {
                await Console.Out.WriteLineAsync("Usage: mediana [--pdf-png <input> <new-output.png> <page> <max-width> | --pdf-image-png <input> <new-output.png> <page> <image-index> | --image-png <heic|jp2|jbig2> <input> <new-output.png> <pixel-aspect-width> <pixel-aspect-height> | --svg-png <input> <new-output.png> <width> | --tgs-png <input> <new-output.png> <seconds> <width> | --probe-mp3 <input> | --cover-mp3 <input> <new-image-output> <picture-index> | --probe-flac <input> | --cover-flac <input> <new-image-output> <picture-index> | --probe-mp4 <path> | --cover-mp4 <input> <new-image-output> <cover-index> | --decode-audio <input> <new-output.pcm> <stream-index> | --resample-audio <input> <new-output.pcm> <stream-index> <sample-rate> <comma-separated-channel-gains> | --waveform-audio <input> <new-output.json> <stream-index> <sample-rate> <samples-per-window> <comma-separated-channel-gains> | --preview <input> <new-output.png> <stream-index> <milliseconds> <max-width> <max-height> | --remux-mp4 <input> <new-output.ts> | --plan-hls-mp4 <input> <stream-index> <target-milliseconds> | --hls-mp4 <input> <new-directory> <video-index> <audio-index|none> <target-milliseconds>]");
                return 0;
            }
            await Console.Error.WriteLineAsync("Usage: mediana [--pdf-png <input> <new-output.png> <page> <max-width> | --pdf-image-png <input> <new-output.png> <page> <image-index> | --image-png <heic|jp2|jbig2> <input> <new-output.png> <pixel-aspect-width> <pixel-aspect-height> | --svg-png <input> <new-output.png> <width> | --tgs-png <input> <new-output.png> <seconds> <width> | --probe-mp3 <input> | --cover-mp3 <input> <new-image-output> <picture-index> | --probe-flac <input> | --cover-flac <input> <new-image-output> <picture-index> | --probe-mp4 <path> | --cover-mp4 <input> <new-image-output> <cover-index> | --decode-audio <input> <new-output.pcm> <stream-index> | --resample-audio <input> <new-output.pcm> <stream-index> <sample-rate> <comma-separated-channel-gains> | --waveform-audio <input> <new-output.json> <stream-index> <sample-rate> <samples-per-window> <comma-separated-channel-gains> | --preview <input> <new-output.png> <stream-index> <milliseconds> <max-width> <max-height> | --remux-mp4 <input> <new-output.ts> | --plan-hls-mp4 <input> <stream-index> <target-milliseconds> | --hls-mp4 <input> <new-directory> <video-index> <audio-index|none> <target-milliseconds>]");
            return 2;
        }

        private static bool TryAudioIndex(string text, out int? index)
        {
            if (text == "none")
            {
                index = null;
                return true;
            }
            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                index = value;
                return true;
            }
            index = null;
            return false;
        }
    }
}
