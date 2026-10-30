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
using Mediana.IO;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    internal static class AacEncodingTestInput
    {
        public static async Task VerifyAsync(string root, string name, int bitRate, int expectedPackets)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new WaveReaderFactory()).AddAudioDecoder(new PcmDecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".wav")));
            await using AudioSession session = await engine.OpenAudioAsync(source);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using AudioFrame first = await session.ReadFrameAsync() ?? throw new InvalidDataException("The AAC encoding fixture has no PCM.");
            using AudioFrame prepared = AudioSampleConverter.Apply(first, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, allocator);
            using AacEncoder encoder = AacEncoder.Create(prepared.Format, new(1, prepared.Format.SampleRate), allocator, new() { BitRate = bitRate });
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(encoder.Stream, allocator);
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + $".encoded-{bitRate / 1000}k.f32le"));
            int byteOffset = 0;
            int packets = 0;
            long samples = 0;
            double maximumError = 0;
            Write(prepared);
            prepared.Dispose();
            first.Dispose();
            while (await session.ReadFrameAsync() is AudioFrame frame)
            {
                using (frame)
                using (AudioFrame converted = AudioSampleConverter.Apply(frame, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, allocator))
                {
                    Write(converted);
                }
            }
            encoder.CompleteInput();
            Drain();
            if (packets != expectedPackets || byteOffset != reference.Length || encoder.InputSamplesPerChannel != samples ||
                (long)packets * 1024 != samples + encoder.PrimingSamples + encoder.PaddingSamples || maximumError > 0.00002)
            {
                throw new InvalidDataException($"AAC encoding count, delay, padding or independent PCM error differs: packets={packets}, maximum error={maximumError}.");
            }
            decoder.Dispose();
            encoder.Dispose();
            await session.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("AAC encoding retained media storage.");
            }

            void Write(AudioFrame frame)
            {
                if (encoder.TrySend(frame) != CodecInputStatus.Accepted)
                {
                    throw new InvalidOperationException("AAC encoding rejected the next drained input frame.");
                }
                samples += frame.SamplesPerChannel;
                Drain();
            }

            void Drain()
            {
                while (true)
                {
                    CodecOutput<MediaPacket> output = encoder.Receive();
                    if (output.Status != CodecOutputStatus.Available)
                    {
                        return;
                    }
                    using MediaPacket packet = output.Value;
                    if (packet.Data.Length * 8 > (long)bitRate * 1024 / prepared.Format.SampleRate + 1 ||
                        packet.PresentationTime?.Value != (long)(packets - 1) * 1024 || packet.PresentationTime != packet.DecodeTime)
                    {
                        throw new InvalidDataException("AAC output exceeds its frame budget or changes its exact sample clock.");
                    }
                    decoder.TrySend(packet);
                    using AudioFrame decoded = decoder.Receive().Value;
                    ReadOnlySpan<float> actual = MemoryMarshal.Cast<byte, float>(decoded.GetPlane(0).Span);
                    foreach (float value in actual)
                    {
                        if (byteOffset > reference.Length - sizeof(float) || !float.IsFinite(value))
                        {
                            throw new InvalidDataException("AAC output contains unexpected or nonfinite samples.");
                        }
                        float expected = BinaryPrimitives.ReadSingleLittleEndian(reference.AsSpan(byteOffset, sizeof(float)));
                        maximumError = Math.Max(maximumError, Math.Abs(value - expected));
                        byteOffset += sizeof(float);
                    }
                    packets++;
                }
            }
        }
    }
}
