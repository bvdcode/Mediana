using Mediana.Containers.Flac;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class FlacReaderTests
    {
        [Fact]
        public async Task ShortSequentialReadsPreserveCommentsPicturesAndExactCompressedBytes()
        {
            byte[] audio = [0xff, 0xf8, 1, 2, 3, 0, 7, 8, 9];
            byte[] picture = FlacTestInput.Picture();
            byte[] file = FlacTestInput.File(audio, (0, FlacTestInput.StreamInfo()), (1, new byte[5]),
                (4, FlacTestInput.Comments("TITLE=Речь 🎵", "ARTIST=First", "artist=Second", "EMPTY=", "EQUALS=a=b")),
                (6, picture), (6, FlacTestInput.Picture(4, "image/jpeg", [8, 7, 6])));
            MediaBufferAllocator allocator = new(16384);
            await using ShortReadSource source = new(file);
            MediaBuffer retained;
            MediaPacket? retainedPacket = null;
            await using (FlacReader reader = await FlacReader.OpenAsync(source, allocator, new FlacReadOptions { PacketBytes = 3 }))
            {
                Assert.Equal(file.Length - audio.Length, reader.AudioOffset);
                Assert.Equal(48000, reader.Configuration.SampleRate);
                Assert.Equal(2, reader.Configuration.ChannelCount);
                Assert.Equal(16, reader.Configuration.BitsPerSample);
                Assert.Equal(new MediaDuration(17760, new TimeBase(1, 48000)), reader.Duration);
                Assert.Equal("flac", Assert.Single(reader.Streams).CodecId);
                Assert.Equal("Mediana test", reader.Vendor);
                Assert.Equal(new[] { "Речь 🎵", "First", "Second", "", "a=b" }, reader.Comments.Select(item => item.Value));
                Assert.Equal("artist", reader.Comments[2].Name);
                Assert.Equal("Обложка 🎵", reader.Pictures[0].Description);
                Assert.Equal("image/jpeg", reader.Pictures[1].MediaType);
                retained = reader.ReadPicture(0);
                List<byte> actual = [];
                while (true)
                {
                    using MediaPacket? packet = await reader.ReadPacketAsync();
                    if (packet is null)
                    {
                        break;
                    }
                    Assert.Null(packet.PresentationTime);
                    Assert.Null(packet.DecodeTime);
                    Assert.Null(packet.Duration);
                    Assert.Null(packet.IsKeyFrame);
                    actual.AddRange(packet.Data.ToArray());
                    retainedPacket?.Dispose();
                    retainedPacket = packet.Retain();
                }
                Assert.Equal(audio, actual);
                Assert.Null(await reader.ReadPacketAsync());
                using MediaBuffer second = reader.ReadPicture(1);
                Assert.Equal(new byte[] { 8, 7, 6 }, second.Memory.ToArray());
            }
            Assert.False(source.IsDisposed);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, retained.Memory.ToArray());
            Assert.Equal(new byte[] { 9 }, retainedPacket!.Data.ToArray());
            Assert.Equal(picture.Length + 3, allocator.AllocatedBytes);
            retained.Dispose();
            retainedPacket.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task UnknownDurationAndLinkedPictureAreDescriptiveWithoutResourceRetrieval()
        {
            byte[] link = "https://example.invalid/cover.png"u8.ToArray();
            await using ShortReadSource source = new(FlacTestInput.File([], (0, FlacTestInput.StreamInfo(samples: 0)),
                (6, FlacTestInput.Picture(mediaType: "-->", image: link))));
            MediaBufferAllocator allocator = new(16384);
            await using FlacReader reader = await FlacReader.OpenAsync(source, allocator, new FlacReadOptions { PacketBytes = 3 });
            Assert.Null(reader.Duration);
            Assert.Equal("-->", Assert.Single(reader.Pictures).MediaType);
            using MediaBuffer picture = reader.ReadPicture(0);
            Assert.Equal(link, picture.Memory.ToArray());
            Assert.Null(await reader.ReadPacketAsync());
        }

        [Fact]
        public async Task InvalidMetadataAndTruncatedExtentsReleaseAllRetainedStorage()
        {
            byte[] invalidUtf8 = FlacTestInput.Comments("TITLE=a");
            invalidUtf8[^1] = 0xff;
            byte[] invalidPicture = FlacTestInput.Picture();
            invalidPicture[^5] = 0xff;
            byte[][] files =
            [
                FlacTestInput.File([], (4, FlacTestInput.Comments())),
                FlacTestInput.File([], (0, FlacTestInput.StreamInfo()), (0, FlacTestInput.StreamInfo())),
                FlacTestInput.File([], (0, FlacTestInput.StreamInfo()), (4, FlacTestInput.Comments()), (4, FlacTestInput.Comments())),
                FlacTestInput.File([], (0, FlacTestInput.StreamInfo()), (6, FlacTestInput.Picture(1)), (6, FlacTestInput.Picture(1))),
                FlacTestInput.File([], (0, FlacTestInput.StreamInfo()), (6, FlacTestInput.Picture()), (1, [1])),
                FlacTestInput.File([], (0, new byte[33])),
                FlacTestInput.File([], (0, FlacTestInput.StreamInfo()), (2, [1, 2])),
                FlacTestInput.File([], (0, FlacTestInput.StreamInfo()), (3, new byte[17])),
                FlacTestInput.File([], (0, FlacTestInput.StreamInfo()), (4, FlacTestInput.Comments("=bad"))),
                FlacTestInput.File([], (0, FlacTestInput.StreamInfo()), (4, FlacTestInput.Comments("Ю=bad"))),
                FlacTestInput.File([], (0, FlacTestInput.StreamInfo()), (6, invalidPicture)),
                FlacTestInput.File([], (0, FlacTestInput.StreamInfo()), (127, []))
            ];
            foreach (byte[] file in files)
            {
                await RejectAsync<InvalidDataException>(file);
            }
            await RejectAsync<System.Text.DecoderFallbackException>(FlacTestInput.File([], (0, FlacTestInput.StreamInfo()), (4, invalidUtf8)));
            byte[] valid = FlacTestInput.File([], (0, FlacTestInput.StreamInfo()), (6, FlacTestInput.Picture()));
            await RejectAsync<InvalidDataException>(valid[..^1]);
            await RejectAsync<NotSupportedException>(FlacTestInput.File([], (0, FlacTestInput.StreamInfo()), (7, [])));
            await RejectAsync<NotSupportedException>(FlacTestInput.File([], (0, FlacTestInput.StreamInfo()),
                (4, FlacTestInput.Comments("WAVEFORMATEXTENSIBLE_CHANNEL_MASK=0x3"))));
        }

        [Fact]
        public async Task MetadataLimitsAreExplicitAndBorrowedSourcesRemainOpen()
        {
            byte[] file = FlacTestInput.File([], (0, FlacTestInput.StreamInfo()),
                (4, FlacTestInput.Comments("TITLE=Some title", "ARTIST=Some artist")),
                (6, FlacTestInput.Picture()), (6, FlacTestInput.Picture(4)));
            foreach (FlacReadOptions options in new[]
            {
                new FlacReadOptions { MaxMetadataBlocks = 1 },
                new FlacReadOptions { MaxMetadataBytes = 42 },
                new FlacReadOptions { MaxTextBytes = 2 },
                new FlacReadOptions { MaxComments = 1 },
                new FlacReadOptions { MaxPictures = 1 }
            })
            {
                await RejectAsync<NotSupportedException>(file, options);
            }
            MediaBufferAllocator allocator = new(4100);
            await using ShortReadSource source = new(file);
            await Assert.ThrowsAsync<MediaMemoryLimitException>(async () => await FlacReader.OpenAsync(source, allocator));
            Assert.False(source.IsDisposed);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task CancellationIsTerminalAndRetainedPicturesSurviveFailure()
        {
            byte[] block = FlacTestInput.Picture();
            byte[] file = FlacTestInput.File([1, 2, 3], (0, FlacTestInput.StreamInfo()), (6, block));
            MediaBufferAllocator allocator = new(16384);
            await using ShortReadSource source = new(file);
            await using FlacReader reader = await FlacReader.OpenAsync(source, allocator);
            using (MediaBuffer retained = reader.ReadPicture(0))
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.ReadPacketAsync(new CancellationToken(true)));
                Assert.Equal(block.Length, allocator.AllocatedBytes);
                Assert.Equal(new byte[] { 1, 2, 3, 4 }, retained.Memory.ToArray());
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadPacketAsync());
                Assert.Throws<InvalidOperationException>(() => reader.ReadPicture(0));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
            await reader.DisposeAsync();
            Assert.Throws<ObjectDisposedException>(() => reader.ReadPicture(0));
        }

        [Theory]
        [InlineData(0, 4096, 48000, 16)]
        [InlineData(4096, 16, 48000, 16)]
        [InlineData(16, 4096, 48000, 3)]
        public void InvalidStreamInfoIsRejected(int minimum, int maximum, int rate, int precision)
        {
            Assert.Throws<InvalidDataException>(() => FlacConfiguration.ReadStreamInfo(
                FlacTestInput.StreamInfo(minimum: minimum, maximum: maximum, rate: rate, bits: precision)));
        }

        [Fact]
        public async Task ConcurrentOperationsCannotReleaseStorageDuringAnActiveRead()
        {
            byte[] file = FlacTestInput.File([1, 2, 3], (0, FlacTestInput.StreamInfo()), (6, FlacTestInput.Picture()));
            await using GatedByteSource source = new(file);
            MediaBufferAllocator allocator = new(32768);
            await using FlacReader reader = await FlacReader.OpenAsync(source, allocator);
            source.GateReads = true;
            Task<MediaPacket?> pending = reader.ReadPacketAsync().AsTask();
            await source.Entered.Task;
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadPacketAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.DisposeAsync());
            Assert.Throws<InvalidOperationException>(() => reader.ReadPicture(0));
            source.Continue.SetResult();
            using (MediaPacket packet = (await pending)!)
            {
                Assert.Equal(new byte[] { 1, 2, 3 }, packet.Data.ToArray());
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadPicture(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadPicture(1));
            Assert.Null(await reader.ReadPacketAsync());
            await reader.DisposeAsync();
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task CancellationDuringBlockedInputReleasesAllMetadataAndPendingPacketStorage()
        {
            await using GatedByteSource source = new(FlacTestInput.File([1],
                (0, FlacTestInput.StreamInfo()), (6, FlacTestInput.Picture())));
            MediaBufferAllocator allocator = new(32768);
            await using FlacReader reader = await FlacReader.OpenAsync(source, allocator);
            source.GateReads = true;
            using CancellationTokenSource cancellation = new();
            Task<MediaPacket?> pending = reader.ReadPacketAsync(cancellation.Token).AsTask();
            await source.Entered.Task;
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadPacketAsync());
        }

        [Fact]
        public async Task KnownTruncationReservedAudioRateAndInvalidOptionsFailExplicitly()
        {
            byte[] bytes = FlacTestInput.File([], (0, FlacTestInput.StreamInfo()), (6, FlacTestInput.Picture()));
            MediaBufferAllocator allocator = new(16384);
            await using GatedByteSource source = new(bytes[..^1]);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await FlacReader.OpenAsync(source, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
            Assert.Throws<NotSupportedException>(() => FlacConfiguration.ReadStreamInfo(FlacTestInput.StreamInfo(rate: 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FlacReaderFactory(new FlacReadOptions { PacketBytes = 0 }));
            FlacReaderFactory factory = new();
            Assert.True(factory.CanRead("fLaC"u8));
            Assert.False(factory.CanRead("fLa"u8));
            Assert.False(factory.CanRead("OggS"u8));
        }

        private static async Task RejectAsync<TException>(byte[] file, FlacReadOptions? options = null) where TException : Exception
        {
            MediaBufferAllocator allocator = new(65536);
            await using ShortReadSource source = new(file);
            await Assert.ThrowsAsync<TException>(async () => await FlacReader.OpenAsync(source, allocator, options));
            Assert.False(source.IsDisposed);
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
