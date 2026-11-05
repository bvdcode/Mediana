// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Pcm;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    internal static class PcmStorageTestInput
    {
        public static void Verify(AudioSampleFormat sampleFormat)
        {
            const int Samples = 4096;
            const int PrefixBytes = 8;
            AudioFormat format = new(48000, sampleFormat, AudioStorageLayout.Interleaved, "front-left", "front-right");
            int payloadBytes = format.GetBufferSize(Samples);
            int backingBytes = payloadBytes + 2 * PrefixBytes;
            bool native = BitConverter.IsLittleEndian || format.BytesPerSample == 1;
            MediaBufferAllocator sourceAllocator = new(backingBytes);
            MediaBufferAllocator decoderAllocator = new(native ? 1 : payloadBytes);
            using WritableMediaBuffer writable = sourceAllocator.Allocate(backingBytes);
            for (int index = 0; index < backingBytes; index++)
            {
                writable.Memory.Span[index] = (byte)index;
            }
            using MediaBuffer full = writable.Publish();
            using MediaBuffer slice = full.Slice(PrefixBytes, payloadBytes);
            MediaTimestamp timestamp = new(123, new TimeBase(1, format.SampleRate));
            using MediaPacket packet = new(0, slice, timestamp);
            using MediaBuffer lease = packet.RetainData();
            Require(lease.Memory.Equals(packet.Data) && lease.Memory.Length == payloadBytes);
            EncodedStreamInfo stream = new(0, MediaKind.Audio, PcmConfiguration.CodecId,
                StreamFormats.WavePcm, timestamp.TimeBase, PcmConfiguration.WriteWaveFormat(format));
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new PcmDecoderFactory().Create(stream, decoderAllocator);
            Require(decoder.TrySend(packet) == CodecInputStatus.Accepted);
            using AudioFrame frame = decoder.Receive().Value;
            Require(frame.GetPlane(0).Equals(packet.Data) == native);
            Require(frame.Timestamp == timestamp && frame.SamplesPerChannel == Samples);
            Require(decoderAllocator.AllocatedBytes == (native ? 0 : payloadBytes));
            packet.Dispose();
            full.Dispose();
            slice.Dispose();
            decoder.Dispose();
            Require(frame.GetPlane(0).Length == payloadBytes && lease.Memory.Span[0] == PrefixBytes);
            lease.Dispose();
            Require(sourceAllocator.AllocatedBytes == (native ? backingBytes : 0));
            frame.Dispose();
            Require(sourceAllocator.AllocatedBytes == 0 && decoderAllocator.AllocatedBytes == 0);
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("PCM payload retention changed logical bounds, timing, storage budgets or independent ownership.");
            }
        }
    }
}
