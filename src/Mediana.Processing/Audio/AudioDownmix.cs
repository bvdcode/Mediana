// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;

namespace Mediana.Processing.Audio
{
    /// <summary>Creates explicit ITU-R BS.775 mono/stereo downmixes for named front and surround speakers.
    /// A common attenuation bounds both output peaks for inputs in [-1, 1]; LFE is omitted.
    /// Side and back surround labels are alternatives for the same speaker role.</summary>
    public static class AudioDownmix
    {
        private static readonly double MinusThreeDecibels = Math.Sqrt(0.5);

        public static AudioChannelMixer CreateMono(AudioFormat input, AudioSampleFormat outputSampleFormat = AudioSampleFormat.Float64) =>
            Create(input, false, outputSampleFormat);
        public static AudioChannelMixer CreateStereo(AudioFormat input, AudioSampleFormat outputSampleFormat = AudioSampleFormat.Float64) =>
            Create(input, true, outputSampleFormat);

        private static AudioChannelMixer Create(AudioFormat input, bool stereo, AudioSampleFormat outputSampleFormat)
        {
            ArgumentNullException.ThrowIfNull(input);
            string[] channels = stereo ? ["front-left", "front-right"] : ["front-center"];
            int count = input.Channels.Count;
            double[] matrix = new double[checked(count * channels.Length)];
            HashSet<string> roles = new(StringComparer.Ordinal);
            for (int index = 0; index < count; index++)
            {
                string speaker = input.Channels[index];
                (string Role, double Mono, double Left, double Right) contribution = speaker switch
                {
                    "front-left" => ("L", MinusThreeDecibels, 1, 0),
                    "front-right" => ("R", MinusThreeDecibels, 0, 1),
                    "front-center" => ("C", 1, MinusThreeDecibels, MinusThreeDecibels),
                    "mono" when count == 1 => ("C", 1, MinusThreeDecibels, MinusThreeDecibels),
                    "back-left" or "side-left" => ("LS", 0.5, MinusThreeDecibels, 0),
                    "back-right" or "side-right" => ("RS", 0.5, 0, MinusThreeDecibels),
                    "low-frequency" => ("LFE", 0, 0, 0),
                    _ => throw new NotSupportedException($"The standard downmix has no mapping for speaker '{speaker}'; use an explicit channel matrix.")
                };
                if (!roles.Add(contribution.Role))
                {
                    throw new NotSupportedException("The standard downmix requires one speaker per role; use an explicit channel matrix for additional surrounds.");
                }
                if (stereo)
                {
                    matrix[index] = contribution.Left;
                    matrix[count + index] = contribution.Right;
                }
                else
                {
                    matrix[index] = contribution.Mono;
                }
            }
            double scale = 1;
            for (int output = 0; output < channels.Length; output++)
            {
                double sum = 0;
                for (int inputChannel = 0; inputChannel < count; inputChannel++)
                {
                    sum += Math.Abs(matrix[output * count + inputChannel]);
                }
                scale = Math.Max(scale, sum);
            }
            for (int index = 0; index < matrix.Length; index++)
            {
                matrix[index] /= scale;
            }
            return new AudioChannelMixer(input, channels, matrix, outputSampleFormat);
        }
    }
}
