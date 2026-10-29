// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;

namespace Mediana.Codecs.Mp3
{
    internal static class Mp3FrameDecoder
    {
        public static void Decode(ReadOnlySpan<byte> packet, Mp3FrameHeader header, Mp3FrameInfo frame,
            Mp3DecoderWorkspace workspace, Span<float> output, CancellationToken cancellationToken)
        {
            int offset = Mp3FrameHeader.HeaderBytes + (header.HasCrc ? 2 : 0) + header.SideInfoBytes;
            ReadOnlySpan<byte> data = workspace.Reservoir.Assemble(packet[offset..], frame.MainDataBegin,
                frame.MainDataBits, cancellationToken);
            Span<byte> scales = stackalloc byte[2 * Mp3ScaleFactors.Capacity];
            Span<byte> limits = stackalloc byte[Mp3ScaleFactors.Capacity];
            Span<int> quantized = stackalloc int[576];
            Span<Mp3SpectralBand> layouts = stackalloc Mp3SpectralBand[2 * Mp3ScaleFactors.Capacity];
            Span<int> counts = stackalloc int[2];
            Span<double> reordered = stackalloc double[576];
            scales.Clear();
            int start = 0;
            for (int granule = 0; granule < frame.GranuleCount; granule++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int channel = 0; channel < header.ChannelCount; channel++)
                {
                    Mp3GranuleInfo info = frame.Granules[granule * header.ChannelCount + channel];
                    Span<byte> factors = scales.Slice(channel * Mp3ScaleFactors.Capacity, Mp3ScaleFactors.Capacity);
                    Mp3BitReader bits = new(data, start, info.PartBits);
                    switch (header.Version)
                    {
                        case MpegAudioVersion.Mpeg1:
                            int reuse = granule == 0 ? 0 : channel == 0 ? frame.LeftScaleFactorReuse : frame.RightScaleFactorReuse;
                            Mp3ScaleFactors.ReadMpeg1(ref bits, info, reuse, factors);
                            limits.Fill(byte.MaxValue);
                            break;
                        case MpegAudioVersion.Mpeg2:
                        case MpegAudioVersion.Mpeg25:
                            bool intensity = header.ChannelMode == Mp3ChannelMode.JointStereo && (header.ModeExtension & 1) != 0 && channel == 1;
                            Mp3ScaleFactors.ReadLowRate(ref bits, info, intensity, factors, limits);
                            break;
                        default:
                            throw new InvalidOperationException("The MP3 version is invalid.");
                    }
                    Mp3Spectrum.Read(ref bits, header.SampleRate, info, quantized);
                    Span<Mp3SpectralBand> layout = layouts.Slice(channel * Mp3ScaleFactors.Capacity, Mp3ScaleFactors.Capacity);
                    counts[channel] = Mp3SpectralLayout.Write(header.SampleRate, info, layout);
                    Mp3Requantization.Apply(quantized, factors, info, layout[..counts[channel]], workspace.Spectrum(channel));
                    start = bits.Position;
                }
                if (header.ChannelCount == 2)
                {
                    Mp3Stereo.Apply(header, frame.Granules[granule * 2], frame.Granules[granule * 2 + 1],
                        layouts[..counts[0]], scales[Mp3ScaleFactors.Capacity..], limits, workspace.Spectrum(0), workspace.Spectrum(1));
                }
                for (int channel = 0; channel < header.ChannelCount; channel++)
                {
                    Mp3GranuleInfo info = frame.Granules[granule * header.ChannelCount + channel];
                    Mp3SpectralLayout.Reorder(workspace.Spectrum(channel),
                        layouts.Slice(channel * Mp3ScaleFactors.Capacity, counts[channel]), reordered);
                    Mp3HybridSynthesis.Process(reordered, info, header.SampleRate, workspace.Overlap(channel), workspace.Subbands);
                    Mp3PolyphaseSynthesis.Process(workspace.Subbands, workspace.History(channel),
                        ref workspace.SynthesisPositions[channel], output[(granule * 576 * header.ChannelCount)..], header.ChannelCount, channel);
                }
            }
        }
    }
}
