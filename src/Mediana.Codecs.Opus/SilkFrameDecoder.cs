// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Opus
{
    internal class SilkFrameDecoder : IDisposable
    {
        private readonly SilkChannelDecoder mid, side;
        private readonly SilkResampler leftResampler, rightResampler;
        private readonly SilkStereoState stereo = new();
        private readonly WritableMediaBuffer storage;
        private readonly int planeSamples;
        private int previousChannels;
        private bool previousMidOnly, sampleRateChanged, disposed, faulted;

        public SilkFrameDecoder(int sampleRate, int outputChannels, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            if (outputChannels is not (1 or 2)) { throw new ArgumentOutOfRangeException(nameof(outputChannels)); }
            _ = new SilkFrameMode(sampleRate, 4);
            SampleRate = sampleRate; OutputChannels = outputChannels; planeSamples = SilkFrameMode.MaximumFrameSamples;
            SilkChannelDecoder createdMid = new(sampleRate, allocator);
            SilkChannelDecoder? createdSide = null;
            SilkResampler? createdLeft = null, createdRight = null;
            bool succeeded = false;
            try
            {
                createdSide = new(sampleRate, allocator); createdLeft = new(sampleRate, allocator); createdRight = new(sampleRate, allocator);
                storage = allocator.Allocate((2 * planeSamples + 960) * sizeof(short));
                mid = createdMid; side = createdSide; leftResampler = createdLeft; rightResampler = createdRight;
                succeeded = true;
            }
            finally
            {
                if (!succeeded) { createdRight?.Dispose(); createdLeft?.Dispose(); createdSide?.Dispose(); createdMid.Dispose(); }
            }
        }

        public int SampleRate { get; private set; }
        public int OutputChannels { get; }
        public uint FinalRange { get; private set; }

        public int Decode(ReadOnlySpan<byte> frame, OpusPacketConfiguration configuration, Span<float> output,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) { throw new InvalidOperationException("Reset the SILK decoder after a failed frame."); }
            if (configuration.CodingMode != OpusCodingMode.Silk) { throw new ArgumentException("The frame does not use standalone SILK coding.", nameof(configuration)); }
            int rate = configuration.Bandwidth switch
            {
                OpusBandwidth.Narrowband => 8000,
                OpusBandwidth.Mediumband => 12000,
                OpusBandwidth.Wideband => 16000,
                OpusBandwidth.SuperWideband or OpusBandwidth.Fullband => throw new ArgumentException("Standalone SILK cannot carry the specified bandwidth.", nameof(configuration)),
                _ => throw new ArgumentOutOfRangeException(nameof(configuration))
            };
            if (rate != SampleRate) { throw new ArgumentException("The SILK frame has a different internal sample rate.", nameof(configuration)); }
            if (frame.Length <= 1) { throw new NotSupportedException("A missing SILK frame requires packet-loss reconstruction."); }
            ArgumentOutOfRangeException.ThrowIfGreaterThan(frame.Length, 1275, nameof(frame));
            OpusRangeDecoder range = new(frame);
            int samples = DecodeLayer(ref range, configuration.CodedChannelCount, configuration.SamplesPerFrame, output, cancellationToken);
            if (range.BitsUsed + 17 <= range.FrameBitCount)
            {
                faulted = true;
                throw new NotSupportedException("A SILK frame with CELT transition redundancy requires the combined Opus decoder.");
            }
            return samples;
        }

        public int DecodeLayer(ref OpusRangeDecoder range, int channels, int samples, Span<float> output,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) { throw new InvalidOperationException("Reset the SILK decoder after a failed frame."); }
            if (channels is not (1 or 2)) { throw new ArgumentOutOfRangeException(nameof(channels)); }
            if (samples is not (480 or 960 or 1920 or 2880)) { throw new ArgumentOutOfRangeException(nameof(samples)); }
            ArgumentOutOfRangeException.ThrowIfLessThan(output.Length, samples * OutputChannels, nameof(output));
            cancellationToken.ThrowIfCancellationRequested();
            bool succeeded = false;
            try
            {
                int frames = (samples + 959) / 960, subframes = 4;
                if (samples == 480) { subframes = 2; }
                SilkFrameMode mode = new(SampleRate, subframes);
                if (channels > previousChannels) { side.Reset(); }
                SilkPayloadHeader header = SilkPayloadHeader.Read(ref range, channels, frames);
                SkipRedundancy(ref range, mode, channels, frames, header);
                for (int index = 0; index < frames; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    DecodeBlock(ref range, mode, channels, index, header, output.Slice(index * 960 * OutputChannels));
                }
                FinalRange = range.Range; succeeded = true;
                return samples;
            }
            finally { if (!succeeded) { faulted = true; } }
        }

        private void SkipRedundancy(ref OpusRangeDecoder range, SilkFrameMode mode, int channels, int frames, SilkPayloadHeader header)
        {
            for (int index = 0; index < frames; index++)
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    if (!header.IsRedundant(channel, index)) { continue; }
                    if (channels == 2 && channel == 0)
                    {
                        SilkStereoReader.ReadPrediction(ref range);
                        if (!header.IsRedundant(1, index)) { SilkStereoReader.ReadMidOnly(ref range); }
                    }
                    SilkCodingMode coding = SilkCodingMode.Independent;
                    if (index > 0 && header.IsRedundant(channel, index - 1)) { coding = SilkCodingMode.Conditional; }
                    if (channel == 0) { mid.Skip(ref range, mode, coding); }
                    else { side.Skip(ref range, mode, coding); }
                }
            }
        }

        private void DecodeBlock(ref OpusRangeDecoder range, SilkFrameMode mode, int channels, int index,
            SilkPayloadHeader header, Span<float> output)
        {
            bool collapsing = previousChannels == 2 && channels == 1 && !sampleRateChanged;
            sampleRateChanged = false;
            if (OutputChannels == 2 && channels == 2 && previousChannels < 2)
            {
                stereo.Prediction = default; stereo.SidePrevious1 = stereo.SidePrevious2 = 0;
                rightResampler.CopyFrom(leftResampler);
            }
            SilkStereoPrediction prediction = default;
            bool midOnly = false;
            if (channels == 2)
            {
                prediction = SilkStereoReader.ReadPrediction(ref range);
                if (!header.IsActive(1, index)) { midOnly = SilkStereoReader.ReadMidOnly(ref range); }
                if (!midOnly && previousMidOnly) { side.ResetPrediction(); }
            }
            Span<short> values = MemoryMarshal.Cast<byte, short>(storage.Span);
            Span<short> midSamples = values[..mode.FrameSamples], sideSamples = values.Slice(planeSamples, mode.FrameSamples);
            SilkCodingMode midCoding = SilkCodingMode.Independent, sideCoding = SilkCodingMode.Independent;
            if (index > 0)
            {
                midCoding = SilkCodingMode.Conditional;
                if (previousMidOnly) { sideCoding = SilkCodingMode.IndependentWithoutLtpScaling; }
                else { sideCoding = SilkCodingMode.Conditional; }
            }
            mid.Decode(ref range, mode, midCoding, header.IsActive(0, index), midSamples);
            if (channels == 2 && !midOnly) { side.Decode(ref range, mode, sideCoding, header.IsActive(1, index), sideSamples); }
            else { sideSamples.Clear(); }
            if (OutputChannels == 2 && channels == 2) { SilkStereoSynthesis.Unmix(stereo, mode, prediction, midSamples, sideSamples); }
            else { SilkStereoSynthesis.DelayMono(stereo, midSamples); }
            Span<short> converted = values[(2 * planeSamples)..];
            int written = leftResampler.Process(midSamples, converted);
            for (int at = 0; at < written; at++) { output[at * OutputChannels] = converted[at] * (1f / 32768); }
            if (OutputChannels == 2)
            {
                if (channels == 2) { rightResampler.Process(sideSamples, converted); }
                else if (collapsing) { rightResampler.Process(midSamples, converted); }
                for (int at = 0; at < written; at++) { output[2 * at + 1] = converted[at] * (1f / 32768); }
            }
            previousChannels = channels; previousMidOnly = midOnly;
        }

        public void SetSampleRate(int sampleRate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (sampleRate == SampleRate) { return; }
            mid.SetSampleRate(sampleRate); side.SetSampleRate(sampleRate);
            leftResampler.SetSampleRate(sampleRate); rightResampler.SetSampleRate(sampleRate);
            SampleRate = sampleRate; sampleRateChanged = true;
        }

        public void Reset()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            mid.Reset(); side.Reset(); leftResampler.Reset(); rightResampler.Reset(); stereo.Reset();
            storage.Span.Clear(); previousChannels = 0; previousMidOnly = false; sampleRateChanged = false; FinalRange = 0; faulted = false;
        }

        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true; storage.Dispose(); rightResampler.Dispose(); leftResampler.Dispose(); side.Dispose(); mid.Dispose();
        }
    }
}
