// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using ConsoleAppFramework;

namespace Mediana.Cli
{
    internal class AudioCommands : MediaCommands
    {
        /// <summary>Decode supported WAVE, AIFF, AAC, FLAC or MP3 to raw native-endian PCM.</summary>
        /// <param name="input">Existing input media file.</param>
        /// <param name="output">New raw PCM file.</param>
        /// <param name="stream">Audio stream index.</param>
        public Task<int> Decode([Argument] string input, [Argument] string output, int stream = 0, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(stream);
            return ExecuteAsync(input, output, () => AudioDecodeCommand.RunAsync(input, output, stream, cancellationToken));
        }

        /// <summary>Mix to mono, resample and export raw PCM16LE.</summary>
        /// <param name="input">Existing input media file.</param>
        /// <param name="output">New raw PCM16LE file.</param>
        /// <param name="stream">Audio stream index.</param>
        /// <param name="sampleRate">Output sample rate in Hz.</param>
        /// <param name="gains">One finite mix gain per channel, separated by spaces.</param>
        public Task<int> Resample([Argument] string input, [Argument] string output, int stream = 0, int sampleRate = 400,
            CancellationToken cancellationToken = default, params double[] gains)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(stream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
            ValidateGains(gains);
            return ExecuteAsync(input, output, () => AudioResampleCommand.RunAsync(input, output, stream, sampleRate, gains, cancellationToken));
        }

        /// <summary>Export mono PCM16 RMS/peak windows to JSON.</summary>
        /// <param name="input">Existing input media file.</param>
        /// <param name="output">New waveform JSON file.</param>
        /// <param name="stream">Audio stream index.</param>
        /// <param name="sampleRate">Output sample rate in Hz.</param>
        /// <param name="samplesPerWindow">Number of mono samples per analysis window.</param>
        /// <param name="gains">One finite mix gain per channel, separated by spaces.</param>
        public Task<int> Waveform([Argument] string input, [Argument] string output, int stream = 0, int sampleRate = 400,
            int samplesPerWindow = 40, CancellationToken cancellationToken = default, params double[] gains)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(stream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samplesPerWindow);
            ValidateGains(gains);
            return ExecuteAsync(input, output, () => AudioWaveformCommand.RunAsync(input, output, stream, sampleRate,
                samplesPerWindow, gains, cancellationToken));
        }
    }
}
