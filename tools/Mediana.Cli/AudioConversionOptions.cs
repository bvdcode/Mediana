// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>


namespace Mediana.Cli
{
    internal record AudioConversionOptions(AudioCodecMode? Codec, int? BitRate, int? Channels)
    {
        public bool IsSpecified => Codec is not null || BitRate is not null || Channels is not null;
        public AudioConversionRequest Create(string extension)
        {
            AudioCodecMode selected = Codec ?? extension switch
            {
                ".mp3" => AudioCodecMode.Mp3,
                _ => throw new NotSupportedException("Select --audio-codec Aac for .ts output or MP3 encoding for .mp3 output.")
            };
            bool valid = selected switch
            {
                AudioCodecMode.Aac => extension == ".ts",
                AudioCodecMode.Mp3 => extension == ".mp3",
                _ => throw new ArgumentOutOfRangeException(nameof(Codec))
            };
            if (!valid) { throw new NotSupportedException("The selected audio codec does not match the output container."); }
            if (Channels is not null && Channels is not (1 or 2))
            {
                throw new ArgumentOutOfRangeException(nameof(Channels), "Select one or two output audio channels.");
            }
            return new(selected, BitRate ?? 128000, Channels);
        }
    }
}
