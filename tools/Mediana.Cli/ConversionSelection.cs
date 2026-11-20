// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>


namespace Mediana.Cli
{
    internal record ConversionSelection(VideoConversionOptions Video, AudioConversionOptions Audio, ImageConversionOptions Image)
    {
        public bool HasOptions => Video.IsSpecified || Audio.IsSpecified || Image.IsSpecified;

        public ConversionRequest Create(string output)
        {
            if (!Enum.IsDefined(Video.Codec)) { throw new ArgumentOutOfRangeException(nameof(Video)); }
            int selected = (Video.IsSpecified ? 1 : 0) + (Audio.IsSpecified ? 1 : 0) + (Image.IsSpecified ? 1 : 0);
            if (selected > 1)
            {
                if (Image.IsSpecified) { throw new ArgumentException("Image encoding options cannot be combined with audio or video encoding options."); }
                throw new NotSupportedException("Audio-only encoding options cannot be combined with video encoding options.");
            }
            string extension = Path.GetExtension(output).ToLowerInvariant();
            if (Video.IsSpecified) { return Video.Create(extension); }
            if (Audio.IsSpecified) { return Audio.Create(extension); }
            if (Image.IsSpecified) { return Image.Create(output); }
            return extension switch
            {
                ".mp3" => Audio.Create(extension),
                ".wav" or ".wave" => new WaveConversionRequest(),
                ".png" or ".webp" or ".jpg" or ".jpeg" => Image.Create(output),
                ".ts" => new CopyConversionRequest(),
                _ => throw new NotSupportedException("Cannot select an output format from the extension '" + Path.GetExtension(output)
                    + "'. Use .wav or .wave (PCM16), .png, .jpg, .jpeg or .webp (one image/frame/page), .mp3 (Layer III audio), or .ts (AVC/AAC/MP3 stream copy).")
            };
        }
    }
}
