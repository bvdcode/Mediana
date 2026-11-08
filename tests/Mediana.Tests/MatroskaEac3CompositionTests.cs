// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Matroska;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class MatroskaEac3CompositionTests
    {
        [Theory]
        [InlineData(8, false)]
        [InlineData(16, false)]
        [InlineData(8, true)]
        [InlineData(16, true)]
        public Task IndividualSyncframesRetainExactProgrammeTimingMetadataAndIndexedRecovery(int channels, bool laced) =>
            Ac3MatroskaTestInput.VerifyAsync(channels, laced);

        [Theory]
        [InlineData("orphan")]
        [InlineData("order")]
        [InlineData("incomplete")]
        [InlineData("sampling")]
        [InlineData("channels")]
        public async Task InvalidProgrammeHeadersReleaseTheIndexAndBorrowedSource(string violation)
        {
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Ac3", "dependent-16.eac3"));
            List<byte[]> frames = Ac3MatroskaTestInput.Frames(encoded);
            int channels = 16;
            switch (violation)
            {
                case "orphan": frames.RemoveAt(0); break;
                case "order": (frames[1], frames[2]) = (frames[2], frames[1]); break;
                case "incomplete": frames.RemoveAt(frames.Count - 1); break;
                case "sampling": frames[1][4] |= 0x40; break;
                case "channels": channels = 2; break;
                default: throw new ArgumentOutOfRangeException(nameof(violation));
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using EbmlElementTestSource source = new(Ac3MatroskaTestInput.Create(frames, channels, true));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await MatroskaReader.OpenAsync(source, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task AdditionalIndependentProgrammesRequireExplicitSelection()
        {
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Ac3", "dependent-8.eac3"));
            List<byte[]> frames = Ac3MatroskaTestInput.Frames(encoded);
            frames[0][2] |= 0x08;
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using EbmlElementTestSource source = new(Ac3MatroskaTestInput.Create(frames, 8, false));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await MatroskaReader.OpenAsync(source, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Theory]
        [InlineData(8)]
        [InlineData(16)]
        public async Task ExplicitLacedBlockDurationClipsEveryChannelOfTheFinalFrameSet(int channels)
        {
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Ac3", $"dependent-{channels}.eac3"));
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Ac3", $"dependent-{channels}.pcm"));
            List<byte[]> frames = Ac3MatroskaTestInput.Frames(encoded);
            byte[] data = Ac3MatroskaTestInput.Create(frames, channels, true, true);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using EbmlElementTestSource source = new(data);
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator))
            {
                int first = frames.Count - (channels == 8 ? 2 : 4);
                for (int index = first; index < frames.Count; index++)
                {
                    MediaDuration duration = reader.GetPacketTiming(0, index).Duration!.Value;
                    Assert.Equal(0, new MediaTimestamp(duration.Value, duration.TimeBase).CompareTo(new(576, new(1, 48000))));
                }
            }
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory())
                .AddAudioDecoder(new Mediana.Codecs.Ac3.Eac3DecoderFactory(false)).Build();
            await using (AudioSession session = await engine.OpenAudioAsync(source))
            {
                int samples = 0;
                while (await session.ReadFrameAsync() is AudioFrame frame)
                {
                    using (frame)
                    {
                        Assert.Equal(samples == 2304 ? 576 : 768, frame.SamplesPerChannel);
                        Assert.True(frame.GetPlane(0).Span.SequenceEqual(reference.AsSpan(samples * channels * sizeof(float), frame.GetPlane(0).Length)));
                        samples += frame.SamplesPerChannel;
                    }
                }
                Assert.Equal(2880, samples);
            }
            Assert.Equal(0, allocator.AllocatedBytes); Assert.False(source.IsDisposed);
        }
    }
}
