// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Codecs.Pcm;
using Mediana.Containers.Wave;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Audio;
using Mediana.IO;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    internal static class AacSwitchedEncodingTestInput
    {
        private const int SampleRate = 48000;
        private const int SourceSamples = 13312;
        private static ReadOnlySpan<int> Sequences => [0, 1, 2, 2, 3, 1, 2, 2, 3, 0, 1, 3, 0, 0];

        public static EncodedStreamInfo CreateStream(int channels)
        {
            if (channels is not (1 or 2))
            {
                throw new ArgumentOutOfRangeException(nameof(channels));
            }
            return new(0, MediaKind.Audio, AacConfiguration.CodecId, StreamFormats.Aac, new(1, SampleRate),
                AacConfiguration.Create(SampleRate, channels).ToAudioSpecificConfig());
        }

        public static async Task EncodeAsync(string root, int channels, Func<MediaPacket, ValueTask> consume)
        {
            _ = CreateStream(channels);
            float[] samples = await ReadSourceAsync(root);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (AacEncoderWorkspace workspace = new(allocator, channels))
            {
                for (int index = 0; index < Sequences.Length; index++)
                {
                    using MediaPacket packet = EncodePacket(workspace, allocator, samples, index);
                    await consume(packet);
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Switched AAC encoding retained media storage.");
            }
        }

        public static async Task VerifyAsync(string root, int channels)
        {
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, $"window-switch-{channels}.encoded-{channels * 96}k.f32le"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(CreateStream(channels), allocator);
            int byteOffset = 0;
            int packets = 0;
            double maximumError = 0;
            await EncodeAsync(root, channels, ReadPacket);
            decoder.CompleteInput();
            if (decoder.Receive().Status != CodecOutputStatus.EndOfStream || packets != Sequences.Length ||
                reference.Length != Sequences.Length * 1024 * channels * sizeof(float) || byteOffset != reference.Length || maximumError > 0.00002)
            {
                throw new InvalidDataException($"Switched AAC counts, final drain or independent PCM differ: packets={packets}, maximum error={maximumError}.");
            }
            decoder.Dispose();
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Switched AAC decoding retained media storage.");
            }

            ValueTask ReadPacket(MediaPacket packet)
            {
                VerifyWindow(packet, channels, packets);
                MediaTimestamp timestamp = new((packets - 1) * 1024, new(1, SampleRate));
                if (packet.Data.Length * 8 > channels * 2048 || packet.PresentationTime != timestamp || packet.DecodeTime != timestamp ||
                    packet.Duration != new MediaDuration(1024, new(1, SampleRate)) || decoder.TrySend(packet) != CodecInputStatus.Accepted)
                {
                    throw new InvalidDataException("Switched AAC changed its packet budget or sample clock.");
                }
                using AudioFrame decoded = decoder.Receive().Value;
                if (decoded.SamplesPerChannel != 1024 || decoded.Timestamp != timestamp || decoded.Format.Channels.Count != channels)
                {
                    throw new InvalidDataException("Switched AAC changed its decoded frame layout or timing.");
                }
                foreach (float value in MemoryMarshal.Cast<byte, float>(decoded.GetPlane(0).Span))
                {
                    if (byteOffset > reference.Length - sizeof(float) || !float.IsFinite(value))
                    {
                        throw new InvalidDataException("Switched AAC contains unexpected or nonfinite PCM.");
                    }
                    float expected = BinaryPrimitives.ReadSingleLittleEndian(reference.AsSpan(byteOffset, sizeof(float)));
                    maximumError = Math.Max(maximumError, Math.Abs(value - expected));
                    byteOffset += sizeof(float);
                }
                if (decoder.Receive().Status != CodecOutputStatus.InputRequired)
                {
                    throw new InvalidOperationException("Switched AAC did not drain its complete access unit.");
                }
                packets++;
                return ValueTask.CompletedTask;
            }
        }

        private static MediaPacket EncodePacket(AacEncoderWorkspace workspace, MediaBufferAllocator allocator, float[] samples, int index)
        {
            for (int channel = 0; channel < workspace.Channels; channel++)
            {
                Span<double> input = workspace.Input(channel);
                for (int position = 0; position < input.Length; position++)
                {
                    int sample = (index - 1) * 1024 + position;
                    input[position] = sample < 0 || sample >= SourceSamples ? 0 : samples[sample * 2 + channel] * 32768.0;
                }
            }
            Span<int> offsets = stackalloc int[AacSpectrumQuantizer.ChannelBands + 1];
            AacEncodingWindow window = new(3, Sequences[index], index % 2, Groups(index), offsets);
            Span<byte> encoded = stackalloc byte[1536];
            int length = AacPacketEncoder.Encode(workspace, window, index == 0 ? 0 : (index - 1) % 2,
                workspace.Channels * 2048, encoded, default);
            using WritableMediaBuffer storage = allocator.Allocate(length);
            encoded[..length].CopyTo(storage.Memory.Span);
            using MediaBuffer buffer = storage.Publish();
            MediaTimestamp timestamp = new((index - 1) * 1024, new(1, SampleRate));
            return new(0, buffer, timestamp, timestamp, new(1024, new(1, SampleRate)));
        }

        private static void VerifyWindow(MediaPacket packet, int channels, int index)
        {
            AacBitReader bits = new(packet.Data.Span);
            if (bits.Read(3) != channels - 1 || bits.Read(4) != 0 || (channels == 2 && bits.Read(1) != 1))
            {
                throw new InvalidDataException("Switched AAC changed its channel element or common-window declaration.");
            }
            if (channels == 1)
            {
                _ = bits.Read(8);
            }
            AacWindowInfo window = AacWindowInfo.Read(ref bits, 3);
            if (window.Sequence != Sequences[index] || window.Shape != index % 2 || !window.Groups.AsSpan().SequenceEqual(Groups(index)) ||
                (channels == 2 && bits.Read(2) != 0))
            {
                throw new InvalidDataException("Switched AAC changed its window sequence, shape or grouping.");
            }
        }

        private static ReadOnlySpan<int> Groups(int index) => index switch
        {
            2 => [8],
            3 => [1, 1, 1, 1, 1, 1, 1, 1],
            6 => [1, 3, 2, 2],
            7 => [3, 3, 2],
            _ => [1]
        };

        private static async Task<float[]> ReadSourceAsync(string root)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new WaveReaderFactory()).AddAudioDecoder(new PcmDecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, "window-switch.wav")));
            await using AudioSession session = await engine.OpenAudioAsync(source);
            MediaBufferAllocator allocator = new(1024 * 1024);
            float[] samples = new float[SourceSamples * 2];
            int offset = 0;
            while (await session.ReadFrameAsync() is AudioFrame frame)
            {
                using (frame)
                using (AudioFrame converted = AudioSampleConverter.Apply(frame, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, allocator))
                {
                    ReadOnlySpan<float> input = MemoryMarshal.Cast<byte, float>(converted.GetPlane(0).Span);
                    if (converted.Format.SampleRate != SampleRate || converted.Format.Channels.Count != 2 || offset > samples.Length - input.Length)
                    {
                        throw new InvalidDataException("Switched AAC requires the complete declared stereo PCM fixture.");
                    }
                    input.CopyTo(samples.AsSpan(offset));
                    offset += input.Length;
                }
            }
            await session.DisposeAsync();
            if (offset != samples.Length || allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidDataException("Switched AAC source count or storage lifetime differs.");
            }
            return samples;
        }
    }
}
