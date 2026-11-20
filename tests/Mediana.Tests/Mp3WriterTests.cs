// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs;
using Mediana.Codecs.Mp3;
using Mediana.Containers.Mp3;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    public class Mp3WriterTests
    {
        [Fact]
        public async Task AviAndMp4ConvertThroughRegisteredFactoriesWithExactPresentation()
        {
            await Mp3FileTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Avi", "mjpeg-pcm.avi"), 72000);
            await Mp3FileTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4", "avc-aac.mp4"));
        }

        [Theory]
        [InlineData(48000, 2, 192000, 4097)]
        [InlineData(22050, 1, 64000, 1)]
        [InlineData(8000, 2, 32000, 576)]
        public async Task RoundTripPreservesExplicitSampleSelectionAndBorrowedResources(int rate, int channels, int bitRate, int samples)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            AudioFormat format = new(rate, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved,
                channels == 1 ? ["front-center"] : ["front-left", "front-right"]);
            using MemoryStream output = new();
            using (Mp3Encoder encoder = Mp3Encoder.Create(format, new(1, rate), allocator, new() { BitRate = bitRate }))
            {
                await using Mp3Writer writer = Mp3Writer.Create(output, [encoder.Stream], allocator);
                using WritableMediaBuffer storage = allocator.Allocate(samples * channels * sizeof(float));
                Span<float> pcm = MemoryMarshal.Cast<byte, float>(storage.Memory.Span);
                for (int index = 0; index < pcm.Length; index++) { pcm[index] = (float)(0.5 * Math.Sin(index * 0.3)); }
                using MediaBuffer buffer = storage.Publish();
                using AudioFrame frame = new(format, samples, buffer, new(0, new(1, rate)));
                Assert.Equal(CodecInputStatus.Accepted, encoder.TrySend(frame));
                encoder.CompleteInput();
                while (true)
                {
                    CodecOutput<MediaPacket> result = encoder.Receive();
                    if (result.Status == CodecOutputStatus.EndOfStream) { break; }
                    Assert.Equal(CodecOutputStatus.Available, result.Status);
                    using MediaPacket packet = result.Value;
                    await writer.WritePacketAsync(packet);
                }
                await writer.CompleteAsync();
                Assert.True(output.CanWrite);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            output.Position = 0;
            await using RandomAccessStreamByteSource source = new(output, leaveOpen: true);
            await using (Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator))
            {
                MediaTimeRange range = reader.GetPresentationRange(0)!.Value;
                Assert.Equal(new MediaTimestamp(1057, new(1, rate)), range.Start);
                Assert.Equal(new MediaTimestamp(1057 + samples, new(1, rate)), range.End);
                long packets = 0;
                while (true)
                {
                    using MediaPacket? packet = await reader.ReadPacketAsync();
                    if (packet is null) { break; }
                    packets++;
                }
                Assert.Equal((long)reader.VbrInfo!.FrameCount!, packets);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            output.Position = 0;
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp3ReaderFactory()).AddAudioDecoder(new Mp3DecoderFactory()).Build();
            await using (PreparedAudioSource prepared = await PreparedAudioSource.OpenAsync(engine, source, allocator))
            {
                long count = 0;
                while (true)
                {
                    using AudioFrame? frame = await prepared.ReadFrameAsync(default);
                    if (frame is null) { break; }
                    count += frame.SamplesPerChannel;
                }
                Assert.Equal(samples, count);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
