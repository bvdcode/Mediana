// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Mp3;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class Mp3EncoderLifecycleTests
    {
        [Fact]
        public void ResetReleasesPendingInputAndReproducesEncodedBytes()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            AudioFormat format = new(48000, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, ["front-center"]);
            using Mp3Encoder encoder = Mp3Encoder.Create(format, new(1, 48000), allocator);
            byte[] first = EncodeSilence(encoder, format, allocator, 17);
            encoder.Reset();
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Null(encoder.PaddingSamples);
            byte[] second = EncodeSilence(encoder, format, allocator, 17);
            Assert.Equal(first, second);
            encoder.Reset();
            using WritableMediaBuffer writable = allocator.Allocate(1152 * 4);
            writable.Memory.Span.Clear();
            using MediaBuffer buffer = writable.Publish();
            using AudioFrame frame = new(format, 1152, buffer, new(0, new(1, 48000)));
            Assert.Equal(CodecInputStatus.Accepted, encoder.TrySend(frame));
            frame.Dispose(); buffer.Dispose();
            Assert.NotEqual(0, allocator.AllocatedBytes);
            encoder.Reset();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void CancellationFaultsAndClearsUntilReset()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            AudioFormat format = new(48000, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, ["front-center"]);
            using Mp3Encoder encoder = Mp3Encoder.Create(format, new(1, 48000), allocator);
            using WritableMediaBuffer writable = allocator.Allocate(4);
            writable.Memory.Span.Clear();
            using MediaBuffer buffer = writable.Publish();
            using AudioFrame frame = new(format, 1, buffer, new(0, new(1, 48000)));
            encoder.TrySend(frame);
            frame.Dispose(); buffer.Dispose();
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() => encoder.Receive(cancelled.Token));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Throws<InvalidOperationException>(() => encoder.Receive());
            encoder.Reset();
            encoder.CompleteInput();
            Assert.Equal(CodecOutputStatus.EndOfStream, encoder.Receive().Status);
            Assert.Equal(0, encoder.PaddingSamples);
        }

        [Fact]
        public void HuffmanSelectionPreservesEscapesSignsAndExactBitCounts()
        {
            foreach (int maximum in new[] { 1, 2, 3, 7, 15, 16, 23, 79, 270, 1100, 8206 })
            {
                int[] values = [0, maximum, -maximum, 1, -1, 0, maximum, -maximum];
                int bits = Mp3HuffmanEncoder.Select(values, out int table);
                byte[] output = new byte[128];
                Mp3BitWriter writer = new(output);
                Mp3HuffmanEncoder.Write(ref writer, values, table);
                Assert.Equal(bits, writer.Position);
                Mp3BitReader reader = new(output, 0, bits);
                for (int index = 0; index < values.Length; index += 2)
                {
                    (int x, int y) = Mp3Huffman.ReadPair(ref reader, table);
                    Assert.Equal(values[index], x);
                    Assert.Equal(values[index + 1], y);
                }
                Assert.Equal(0, reader.Remaining);
            }
        }

        private static byte[] EncodeSilence(Mp3Encoder encoder, AudioFormat format, MediaBufferAllocator allocator, int samples)
        {
            using WritableMediaBuffer writable = allocator.Allocate(samples * 4);
            writable.Memory.Span.Clear();
            using MediaBuffer buffer = writable.Publish();
            using AudioFrame frame = new(format, samples, buffer, new(0, new(1, 48000)));
            encoder.TrySend(frame);
            encoder.CompleteInput();
            using MemoryStream result = new();
            while (true)
            {
                CodecOutput<MediaPacket> output = encoder.Receive();
                if (output.Status == CodecOutputStatus.EndOfStream) { break; }
                using MediaPacket packet = output.Value;
                result.Write(packet.Data.Span);
            }
            return result.ToArray();
        }
    }
}
