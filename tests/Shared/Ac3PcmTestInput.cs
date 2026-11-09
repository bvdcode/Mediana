// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs;
using Mediana.Codecs.Ac3;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    internal static class Ac3PcmTestInput
    {
        public static async Task<(int SamplesPerChannel, double SignalToNoiseRatio, double RmsError, double MaximumError, long MaximumAllocatedBytes)> VerifyAsync(
            string root, string encodedName, string referenceName, int sampleRate, int channels, double minimumSnr, bool individualSyncframes = false)
        {
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, encodedName));
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, referenceName));
            Ac3FrameHeader first = Ac3FrameHeader.Read(encoded);
            IAudioDecoderFactory factory = first.IsEnhanced ? new Eac3DecoderFactory(false) : new Ac3DecoderFactory(false);
            EncodedStreamInfo stream = new(0, MediaKind.Audio, factory.CodecId, first.IsEnhanced ? individualSyncframes ? StreamFormats.Eac3Syncframe : StreamFormats.Eac3 : StreamFormats.Ac3, new(1, sampleRate));
            MediaBufferAllocator allocator = new(1024 * 1024);
            int samples = 0, inputSamples = 0, outputOffset = 0;
            double error = 0, signal = 0, maximumError = 0;
            long maximumAllocated = 0;
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = factory.Create(stream, allocator))
            {
                for (int offset = 0; offset < encoded.Length;)
                {
                    Ac3FrameHeader header = Ac3FrameHeader.Read(encoded.AsSpan(offset));
                    Require(header.SampleRate == sampleRate);
                    int packetBytes = individualSyncframes ? header.FrameBytes : FrameSetBytes(encoded, offset);
                    bool dependent = header.IsEnhanced && header.StreamType == 1;
                    int position = dependent ? inputSamples - header.SamplesPerFrame : inputSamples;
                    using (WritableMediaBuffer writable = allocator.Allocate(packetBytes))
                    {
                        encoded.AsSpan(offset, packetBytes).CopyTo(writable.Span);
                        using MediaBuffer buffer = writable.Publish();
                        using MediaPacket packet = new(0, buffer, new(position, new(1, sampleRate)), duration: new(header.SamplesPerFrame, new(1, sampleRate)));
                        Require(decoder.TrySend(packet) == CodecInputStatus.Accepted);
                    }
                    if (!dependent) { inputSamples += header.SamplesPerFrame; }
                    offset += packetBytes;
                    while (decoder.Receive() is { Status: CodecOutputStatus.Available } available)
                    { using AudioFrame frame = available.Value; Compare(frame); }
                }
                decoder.CompleteInput();
                CodecOutput<AudioFrame> output = decoder.Receive();
                while (output.Status == CodecOutputStatus.Available)
                {
                    using (AudioFrame frame = output.Value) { Compare(frame); }
                    output = decoder.Receive();
                }
                Require(output.Status == CodecOutputStatus.EndOfStream);
            }
            Require(allocator.AllocatedBytes == 0 && samples * channels * sizeof(float) == reference.Length);
            double snr = 10 * Math.Log10(signal / Math.Max(error, double.Epsilon));
            if (snr < minimumSnr)
            { throw new InvalidDataException($"The AC-3 PCM reference differs: SNR {snr:F3} dB; maximum error {maximumError:G9}."); }
            return (samples, snr, Math.Sqrt(error / (samples * channels)), maximumError, maximumAllocated);

            void Compare(AudioFrame frame)
            {
                Ac3FrameHeader header = Ac3FrameHeader.Read(encoded.AsSpan(outputOffset));
                maximumAllocated = Math.Max(maximumAllocated, allocator.AllocatedBytes);
                Require(frame.SamplesPerChannel == header.SamplesPerFrame && frame.Format.Channels.Count == channels);
                Require(frame.Format.SampleRate == sampleRate && frame.Timestamp == new MediaTimestamp(samples, new(1, sampleRate)));
                Require(frame.Duration == new MediaDuration(header.SamplesPerFrame, new(1, sampleRate)));
                ReadOnlySpan<float> actual = MemoryMarshal.Cast<byte, float>(frame.GetPlane(0).Span);
                ReadOnlySpan<float> expected = MemoryMarshal.Cast<byte, float>(reference.AsSpan(samples * channels * sizeof(float), actual.Length * sizeof(float)));
                for (int index = 0; index < actual.Length; index++)
                {
                    Require(float.IsFinite(actual[index]));
                    int expectedIndex = index;
                    if (channels == 6)
                    {
                        ReadOnlySpan<int> order = [0, 2, 1, 4, 5, 3];
                        expectedIndex = index / channels * channels + order[index % channels];
                    }
                    double delta = actual[index] - expected[expectedIndex];
                    error += delta * delta; signal += expected[expectedIndex] * expected[expectedIndex];
                    maximumError = Math.Max(maximumError, Math.Abs(delta));
                }
                samples += header.SamplesPerFrame; outputOffset += FrameSetBytes(encoded, outputOffset);
            }
        }

        private static int FrameSetBytes(byte[] encoded, int offset)
        {
            Ac3FrameHeader first = Ac3FrameHeader.Read(encoded.AsSpan(offset));
            int length = first.FrameBytes;
            if (first.IsEnhanced)
            {
                while (offset + length < encoded.Length)
                {
                    Ac3FrameHeader next = Ac3FrameHeader.Read(encoded.AsSpan(offset + length));
                    if (next.StreamType != 1) { break; }
                    length += next.FrameBytes;
                }
            }
            return length;
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("The AC-3 PCM format, clock, sample count or resource lifetime is incorrect."); }
        }
    }
}
