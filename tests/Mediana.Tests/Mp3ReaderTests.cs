using System.Text;
using Mediana.Containers.Mp3;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class Mp3ReaderTests
    {
        [Theory]
        [InlineData(3, 0)]
        [InlineData(3, 0x80)]
        [InlineData(4, 0)]
        [InlineData(4, 0x10)]
        [InlineData(4, 0x80)]
        public async Task MetadataPicturesFramesAndLeasesSurviveOneByteInput(int version, int flags)
        {
            int encoding = version == 3 ? 1 : 3;
            byte[] text = version == 3 ? [1, 0xff, 0xfe, .. Encoding.Unicode.GetBytes("Звук 🎵")]
                : [3, .. Encoding.UTF8.GetBytes("Звук 🎵")];
            byte[] image = [0x89, 0x50, 0xff, 0xe1, 0xff, 0, 0x12];
            byte[] picture = Mp3TestInput.Picture(image, "Обложка", encoding);
            bool unsynchronized = (flags & 0x80) != 0;
            byte[] body;
            if (version == 4 && unsynchronized)
            {
                body = [.. Mp3TestInput.MetadataFrame("TIT2", Mp3TestInput.Unsynchronize(text), version, 2),
                    .. Mp3TestInput.MetadataFrame("APIC", Mp3TestInput.Unsynchronize(picture), version, 2)];
            }
            else
            {
                body = [.. Mp3TestInput.MetadataFrame("TIT2", text, version),
                    .. Mp3TestInput.MetadataFrame("APIC", picture, version)];
                if (unsynchronized)
                {
                    body = Mp3TestInput.Unsynchronize(body);
                }
            }
            byte[] tag = Mp3TestInput.Tag(version, body, flags);
            byte[] first = Mp3TestInput.Frame(fill: 0x5a);
            byte[] second = Mp3TestInput.Frame("FFFBB264", 0xc3);
            await using ShortReadSource source = new([.. tag, .. first, .. second]);
            MediaBufferAllocator allocator = new(65536);
            MediaBuffer retained;
            MediaPacket retainedPacket;
            await using (Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator))
            {
                Assert.Equal(tag.Length, reader.AudioOffset);
                Assert.Equal("mp3", Assert.Single(reader.Streams).CodecId);
                Assert.Equal("Звук 🎵", Assert.Single(Assert.Single(reader.TextFrames).Values));
                Assert.Equal("Обложка", Assert.Single(reader.Pictures).Description);
                retained = reader.ReadPicture(0);
                Assert.Null(reader.CodedDuration);
                using MediaPacket packet = Assert.IsType<MediaPacket>(await reader.ReadPacketAsync());
                Assert.Equal(first, packet.Data.ToArray());
                Assert.Equal(new MediaTimestamp(0, new TimeBase(1, 44100)), packet.PresentationTime);
                Assert.Equal(packet.PresentationTime, packet.DecodeTime);
                Assert.Equal(new MediaDuration(1152, new TimeBase(1, 44100)), packet.Duration);
                Assert.Null(packet.IsKeyFrame);
                retainedPacket = Assert.IsType<MediaPacket>(await reader.ReadPacketAsync());
                Assert.Equal(second, retainedPacket.Data.ToArray());
                Assert.Equal(1152, retainedPacket.PresentationTime!.Value.Value);
                Assert.Null(await reader.ReadPacketAsync());
                Assert.Null(await reader.ReadPacketAsync());
                Assert.Equal(new MediaDuration(2304, new TimeBase(1, 44100)), reader.CodedDuration);
            }
            Assert.False(source.IsDisposed);
            Assert.Equal(image, retained.Memory.ToArray());
            Assert.Equal(second, retainedPacket.Data.ToArray());
            retained.Dispose();
            retainedPacket.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData("FFFB9064", 44100, 1152)]
        [InlineData("FFF394C0", 24000, 576)]
        [InlineData("FFE318C0", 8000, 576)]
        public async Task XingMetadataIsNotReturnedAsAudio(string header, int rate, int samples)
        {
            byte[] frame = Mp3TestInput.Frame(header);
            await using ShortReadSource source = new([.. Mp3TestInput.Xing(2, header), .. frame, .. frame]);
            MediaBufferAllocator allocator = new(4096);
            await using (Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator))
            {
                Assert.Equal((uint)2, reader.VbrInfo!.FrameCount);
                Assert.Equal(frame.Length, reader.AudioOffset);
                Assert.Equal(new MediaDuration(samples * 2, new TimeBase(1, rate)), reader.CodedDuration);
                for (int index = 0; index < 2; index++)
                {
                    using MediaPacket packet = Assert.IsType<MediaPacket>(await reader.ReadPacketAsync());
                    Assert.Equal(frame, packet.Data.ToArray());
                    Assert.Equal(index * samples, packet.PresentationTime!.Value.Value);
                }
                Assert.Null(await reader.ReadPacketAsync());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task TrailingId3v1RemainsSeparateAndDoesNotOverwriteId3v2()
        {
            byte[] trailer = new byte[128];
            "TAGold title"u8.CopyTo(trailer);
            trailer[126] = 7;
            trailer[127] = 13;
            byte[] tag = Mp3TestInput.Tag(4, Mp3TestInput.MetadataFrame("TIT2", [3, .. "new title"u8.ToArray()]));
            await using ShortReadSource source = new([.. tag, .. Mp3TestInput.Frame(), .. trailer]);
            MediaBufferAllocator allocator = new(4096);
            await using (Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator))
            {
                Assert.Null(reader.TrailingTag);
                using MediaPacket? packet = await reader.ReadPacketAsync();
                Assert.Null(await reader.ReadPacketAsync());
                Assert.Equal("old title", reader.TrailingTag!.Title);
                Assert.Equal((byte)7, reader.TrailingTag.Track);
                Assert.Equal((byte)13, reader.TrailingTag.Genre);
                Assert.Equal("new title", reader.TextFrames[0].Values[0]);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task CompleteFinalPacketSurvivesFaultAndCancellation()
        {
            byte[] frame = Mp3TestInput.Frame();
            await using ShortReadSource source = new([.. frame, 0xff, 0xfb]);
            MediaBufferAllocator allocator = new(4096);
            await using Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator);
            using MediaPacket packet = Assert.IsType<MediaPacket>(await reader.ReadPacketAsync());
            await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadPacketAsync().AsTask());
            Assert.Equal(frame.Length, allocator.AllocatedBytes);
            Assert.Equal(frame, packet.Data.ToArray());
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadPacketAsync().AsTask());
        }

        [Fact]
        public async Task OverlappingReadsAndDisposalAreRejectedAndCancellationReleasesStorage()
        {
            byte[] frame = Mp3TestInput.Frame();
            await using GatedByteSource source = new([.. frame, .. frame]);
            MediaBufferAllocator allocator = new(4096);
            await using Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator);
            using (MediaPacket? packet = await reader.ReadPacketAsync())
            {
                Assert.NotNull(packet);
            }
            source.GateReads = true;
            using CancellationTokenSource cancellation = new();
            Task<MediaPacket?> pending = reader.ReadPacketAsync(cancellation.Token).AsTask();
            await source.Entered.Task;
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadPacketAsync().AsTask());
            Assert.Throws<InvalidOperationException>(() => reader.DisposeAsync());
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        public async Task DeclaredFrameCountMustMatchActualExtent(uint declared)
        {
            byte[] frame = Mp3TestInput.Frame();
            await using ShortReadSource source = new([.. Mp3TestInput.Xing(declared), .. frame, .. frame]);
            MediaBufferAllocator allocator = new(4096);
            await using Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                while (await reader.ReadPacketAsync() is MediaPacket packet)
                {
                    packet.Dispose();
                }
            });
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
