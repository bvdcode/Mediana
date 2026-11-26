// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Security.Cryptography;
using Mediana.Codecs.Dv;
using Mediana.Codecs.Ffv1;
using Mediana.Containers;
using Mediana.Containers.Asf;
using Mediana.Containers.Avi;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    public class ContainerReadSupportTests
    {
        [Theory]
        [InlineData("mixed.ts", "mpegts", "h264", "aac", "E9BFF7B929B80D213B9E0505D8C2BD8F93AD565878601EB2B4FA4853BE5B2571", "CA84EEE6E6C671038EB14E6B120833A377234C5AD0B11E335AC1D6AA431D0608")]
        [InlineData("mixed.mpg", "mpegps", "mpeg2video", "mpeg-audio", "6515B282DC36570CF04C16CF5731AE37D8D27505CC76E711463C23C9E72A7974", "1B8911EAC0B2B8B3EE028C3F6DDF235E8F9085110C38C54F28D606D5F7906FF5")]
        [InlineData("mixed.wmv", "asf", "wmv2", "wmav2", "FDFB2B695A777BC6C831D22B01EBE3567987FABB4137800024ADE3208185C0C6", "27131C6D14BE9B8DB9EEF46C8FB63BD324C67F28084497C856ECC189119D6000")]
        public async Task DemultiplexesBothTracks(string file, string format, string videoCodec, string audioCodec,
            string videoHash, string audioHash)
        {
            IContainerReaderFactory factory = format switch
            {
                "mpegts" => new MpegTsReaderFactory(),
                "mpegps" => new MpegPsReaderFactory(),
                "asf" => new AsfReaderFactory(),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using RandomAccessStreamByteSource source = Open(file);
            await using IContainerReader reader = await factory.OpenAsync(source, allocator);
            Assert.Equal(format, reader.Info.FormatId);
            EncodedStreamInfo video = Assert.Single(reader.Streams, item => item.Kind == MediaKind.Video);
            EncodedStreamInfo audio = Assert.Single(reader.Streams, item => item.Kind == MediaKind.Audio);
            Assert.Equal(videoCodec, video.CodecId);
            Assert.Equal(audioCodec, audio.CodecId);
            using IncrementalHash videoData = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using IncrementalHash audioData = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int timestamps = 0;
            while (await reader.ReadPacketAsync() is MediaPacket packet)
            {
                using (packet)
                {
                    if (packet.PresentationTime is not null) { timestamps++; }
                    IncrementalHash hash = packet.StreamIndex == video.StreamIndex ? videoData : audioData;
                    hash.AppendData(packet.Data.Span);
                }
            }
            Assert.True(timestamps > 0);
            Assert.Equal(videoHash, Convert.ToHexString(videoData.GetHashAndReset()));
            Assert.Equal(audioHash, Convert.ToHexString(audioData.GetHashAndReset()));
            Assert.Equal(0, allocator.AllocatedBytes);
            if (format == "mpegts") { Assert.Equal("eng", reader.Info.StreamInfo![audio.StreamIndex].Language); }
            if (format == "asf") { Assert.Contains(reader.Info.Tags!, tag => tag.Name == "Title" && tag.Values[0] == "Two tracks"); }
        }

        [Theory]
        [InlineData("ffv1-small.avi", "8F8084CBA39D8738FA969B8DF57F7D6419161EAB82CD8F1A3D87A347C27ED4F3", "F2F05A5DDB817239B260B0A7E9F4A8E13634AFF76A6CEB7A002777A4B70AC022")]
        [InlineData("ffv1-slices.avi", "56E52AFA30E34107AC278D2CF3E53A4CA3B7E05D547B14DF39719905582EA530", "5187925BDE933A4EDC2480821F6DCFFF280B31CC061635A65BB494A236D79A0D")]
        public async Task Ffv1DecodesAndSeeksAcrossDependentFrames(string file, string firstHash, string finalHash)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new AviReaderFactory())
                .AddVideoDecoder(new Ffv1DecoderFactory()).AddStreamMetadataReader(new Ffv1StreamMetadataReader()).Build();
            await using RandomAccessStreamByteSource source = Open(file);
            MediaInfo probe = await engine.ProbeAsync(source);
            Assert.Equal("ffv1", Assert.Single(probe.Streams).CodecId);
            await using VideoSession session = await engine.OpenVideoAsync(source);
            using VideoFrame first = Assert.IsType<VideoFrame>(await session.ReadFrameAsync());
            Assert.Equal(VideoPixelFormat.Yuv420P8, first.Format.PixelFormat);
            Assert.Equal(firstHash, Convert.ToHexString(SHA256.HashData(first.Data.Span)));
            MediaTimestamp target = new(4, new(1, 5));
            await session.SeekAsync(target);
            using VideoFrame final = Assert.IsType<VideoFrame>(await session.ReadFrameAtOrAfterAsync(target));
            Assert.Equal(target, final.Timestamp);
            Assert.Equal(finalHash, Convert.ToHexString(SHA256.HashData(final.Data.Span)));
        }

        [Fact]
        public async Task NtscDv25DecodesNative411BeforeAndAfterSeeking()
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new AviReaderFactory())
                .AddVideoDecoder(new DvDecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = Open("dv-ntsc.avi");
            await using FileStream compressed = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "ReadSupport", "dv-ntsc-frames.yuv.gz"));
            await using GZipStream reference = new(compressed, CompressionMode.Decompress);
            await using VideoSession session = await engine.OpenVideoAsync(source);
            using VideoFrame first = Assert.IsType<VideoFrame>(await session.ReadFrameAsync());
            Assert.Equal(VideoPixelFormat.Yuv411P8, first.Format.PixelFormat);
            Assert.Equal((720, 480), (first.Format.Width, first.Format.Height));
            Assert.Equal(new SampleAspectRatio(8, 9), first.Format.SampleAspectRatio);
            MediaTimestamp target = new(2, new(1001, 30000));
            await session.SeekAsync(target);
            using VideoFrame third = Assert.IsType<VideoFrame>(await session.ReadFrameAtOrAfterAsync(target));
            Assert.Equal(target, third.Timestamp);
            byte[] expected = new byte[first.Data.Length];
            foreach (VideoFrame frame in new VideoFrame[] { first, third })
            {
                await reference.ReadExactlyAsync(expected);
                ReadOnlySpan<byte> decoded = frame.Data.Span;
                for (int index = 0; index < expected.Length; index++)
                {
                    Assert.True(Math.Abs(expected[index] - decoded[index]) <= 1);
                }
            }
        }

        private static RandomAccessStreamByteSource Open(string file) =>
            new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "ReadSupport", file)));
    }
}
