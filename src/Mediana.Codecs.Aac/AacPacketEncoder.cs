// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal static class AacPacketEncoder
    {
        public static int Encode(AacEncoderWorkspace workspace, scoped AacEncodingWindow window, int previousShape,
            int frameBits, scoped Span<byte> encoded, CancellationToken token)
        {
            int channels = workspace.Channels;
            AacBitWriter bits = new(encoded[..(frameBits / 8)]);
            bits.Write(channels == 1 ? 0u : 1, 3);
            bits.Write(0, 4);
            if (channels == 2)
            {
                bits.Write(1, 1);
                AacChannelEncoder.WriteWindow(ref bits, window);
                bits.Write(0, 2);
            }
            for (int channel = 0; channel < channels; channel++)
            {
                AacAnalysis.Transform(workspace.Input(channel), window.Sequence, window.Shape, previousShape, workspace.Spectrum(channel),
                    workspace.Window, workspace.Folded, workspace.Fourier, token);
                if (window.ShortWindow)
                {
                    window.Group<double>(workspace.Spectrum(channel), workspace.Folded);
                    workspace.Folded.CopyTo(workspace.Spectrum(channel));
                }
            }
            Span<int> gains = stackalloc int[channels];
            AacSpectrumQuantizer.Quantize(workspace.Spectra, workspace.Window, workspace.Quantized, workspace.Books, workspace.Scales,
                gains, window, frameBits - 64, token);
            for (int channel = 0; channel < channels; channel++)
            {
                AacChannelEncoder.Write(ref bits, gains[channel], workspace.Quantized.Slice(channel * 1024, 1024),
                    workspace.Books.Slice(channel * AacSpectrumQuantizer.ChannelBands, AacSpectrumQuantizer.ChannelBands),
                    workspace.Scales.Slice(channel * AacSpectrumQuantizer.ChannelBands, AacSpectrumQuantizer.ChannelBands),
                    window, channels == 2);
            }
            return bits.Finish();
        }
    }
}
