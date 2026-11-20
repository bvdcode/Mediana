// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs;
using Mediana.Codecs.Mp3;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    internal static class Mp3EncodeTestInput
    {
        public static void Verify(int rate, int channels, int bitRate)
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            VerifyCore(rate, channels, bitRate, allocator);
            Require(allocator.AllocatedBytes == 0, "MP3 encoding retained buffers after disposal.");
        }

        private static void VerifyCore(int rate, int channels, int bitRate, MediaBufferAllocator allocator)
        {
            AudioFormat format = new(rate, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved,
                channels == 1 ? ["front-center"] : ["front-left", "front-right"]);
            using Mp3Encoder encoder = Mp3Encoder.Create(format, new(1, rate), allocator, new() { BitRate = bitRate });
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new Mp3DecoderFactory().Create(encoder.Stream, allocator);
            const int samples = 11520;
            using WritableMediaBuffer input = allocator.Allocate(samples * channels * sizeof(float));
            Span<float> pcm = MemoryMarshal.Cast<byte, float>(input.Memory.Span);
            for (int sample = 0; sample < samples; sample++)
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    pcm[sample * channels + channel] = (float)(0.4 * Math.Sin(2 * Math.PI * (440 + channel * 290) * sample / rate)
                        + 0.1 * Math.Sin(2 * Math.PI * (3100 + channel * 110) * sample / rate));
                }
            }
            using MediaBuffer buffer = input.Publish();
            using AudioFrame frame = new(format, samples, buffer, new(0, new(1, rate)));
            Require(encoder.TrySend(frame) == CodecInputStatus.Accepted, "MP3 encoder refused PCM.");
            encoder.CompleteInput();
            List<float> decoded = [];
            long packets = 0;
            while (true)
            {
                CodecOutput<MediaPacket> result = encoder.Receive();
                if (result.Status == CodecOutputStatus.EndOfStream) { break; }
                Require(result.Status == CodecOutputStatus.Available, "MP3 encoder did not drain.");
                using MediaPacket packet = result.Value;
                Mp3FrameHeader header = Mp3FrameHeader.Read(packet.Data.Span[..4]);
                Require(header.FrameBytes == packet.Data.Length, "MP3 packet length changed.");
                Require(bitRate == header.BitRate, "MP3 bit rate changed.");
                Require(decoder.TrySend(packet) == CodecInputStatus.Accepted, "MP3 decoder refused the encoded packet.");
                CodecOutput<AudioFrame> audio = decoder.Receive();
                Require(audio.Status == CodecOutputStatus.Available, "MP3 decoding produced no PCM.");
                using AudioFrame output = audio.Value;
                decoded.AddRange(MemoryMarshal.Cast<byte, float>(output.GetPlane(0).Span).ToArray());
                packets++;
            }
            Require(samples == encoder.InputSamplesPerChannel, "MP3 input accounting changed.");
            Require(samples + encoder.PrimingSamples + encoder.PaddingSamples == decoded.Count / channels, "MP3 coded extent changed.");
            double error = 0;
            double energy = 0;
            for (int index = 0; index < pcm.Length; index++)
            {
                double difference = decoded[index + encoder.PrimingSamples * channels] - pcm[index];
                error += difference * difference;
                energy += pcm[index] * pcm[index];
            }
            double snr = 10 * Math.Log10(energy / error);
            Require(snr > 25, $"{rate}/{channels}: SNR {snr:F2} dB, {packets} frames");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) { throw new InvalidDataException(message); }
        }
    }
}
