// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>


namespace Mediana.Cli
{
    internal record AudioConversionOptions(AudioCodecMode? Codec, int? BitRate, int? Channels)
    {
        public bool IsSpecified => Codec is not null || BitRate is not null || Channels is not null;
        public AudioConversionRequest Create(string extension)
        {
            if (Codec != AudioCodecMode.Aac || extension != ".ts")
            {
                throw new NotSupportedException("AAC encoding requires .ts output and --audio-codec Aac.");
            }
            if (Channels is not null && Channels is not (1 or 2))
            {
                throw new ArgumentOutOfRangeException(nameof(Channels), "Select one or two output audio channels.");
            }
            return new(BitRate ?? 128000, Channels);
        }
    }
}
