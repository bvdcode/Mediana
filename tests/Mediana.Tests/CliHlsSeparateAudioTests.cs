// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliHlsSeparateAudioTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-separate-audio-").FullName;

        [Theory]
        [InlineData("avc-aac.mp4", "mp4a.40.2")]
        [InlineData("avc-mp3-44.mp4", "mp4a.6B")]
        [InlineData("avc-mp3-22.mp4", "mp4a.69")]
        public async Task CopiedAudioIsSharedOnceAndVideoFilesMatchVideoOnlyEncoding(string file, string codec)
        {
            string output = Path.Combine(directory, "separate");
            (int code, _, _) = await CliTests.RunAsync(["hls", "encode", Data(file), output,
                "--audio-stream", "1", "--separate-audio", "--segment-ms", "500", "--widths", "64", "32"]);
            Assert.Equal(0, code);
            Assert.Single(Directory.GetDirectories(output, "audio-*"));
            string master = await File.ReadAllTextAsync(Path.Combine(output, "index.m3u8"));
            Assert.Single(master.Split('\n'), line => line.StartsWith("#EXT-X-MEDIA:", StringComparison.Ordinal));
            Assert.Equal(2, master.Split("AUDIO=\"audio-0\"").Length - 1);
            Assert.Equal(2, master.Split("CODECS=\"avc1.42C029," + codec + "\"").Length - 1);
            await VerifyPlaylistsAsync(output, 1);

            string reference = Path.Combine(directory, "video-only");
            (int referenceCode, _, _) = await CliTests.RunAsync(["hls", "encode", Data(file), reference,
                "--video-only", "--segment-ms", "500", "--widths", "64", "32"]);
            Assert.Equal(0, referenceCode);
            foreach (string path in Directory.GetFiles(reference, "*.ts", SearchOption.AllDirectories))
            {
                Assert.Equal(await File.ReadAllBytesAsync(path),
                    await File.ReadAllBytesAsync(Path.Combine(output, Path.GetRelativePath(reference, path))));
            }

            MediaBufferAllocator allocator = new(1024 * 1024);
            await using (RandomAccessStreamByteSource source = new(File.OpenRead(Data(file))))
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator,
                new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                List<(int Stream, byte[] Data, long Pts, long Dts)> expected = [];
                for (int sample = 0; sample < reader.GetPacketCount(1); sample++)
                {
                    using MediaPacket packet = await reader.ReadPacketAtAsync(1, sample);
                    expected.Add((1, packet.Data.ToArray(), MpegTsTestInput.Ticks(packet.PresentationTime),
                        MpegTsTestInput.Ticks(packet.DecodeTime)));
                }
                string audioDirectory = Path.Combine(output, "audio-0");
                string playlist = await File.ReadAllTextAsync(Path.Combine(audioDirectory, "index.m3u8"));
                List<byte> bytes = [];
                foreach (string uri in playlist.Split('\n').Where(line => line.EndsWith(".ts", StringComparison.Ordinal)))
                {
                    bytes.AddRange(await File.ReadAllBytesAsync(Path.Combine(audioDirectory, uri)));
                }
                MpegTsTestInput.Verify(bytes.ToArray(), [reader.Streams[1]], expected);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData("96000,96000,96000", 1)]
        [InlineData("96000,128000,96000", 2)]
        public async Task EncodedProfilesAreDeduplicatedAndAssociatedWithTheirConfiguredVideoVariants(string rates, int profiles)
        {
            string output = Path.Combine(directory, "encoded");
            (int code, string text, _) = await CliTests.RunAsync(["hls", "encode", Data("avc-aac.mp4"), output,
                "--audio-stream", "1", "--audio-codec", "Aac", "--audio-bit-rates", rates, "--separate-audio",
                "--segment-ms", "1024", "--widths", "64", "48", "32"]);
            Assert.Equal(0, code);
            Assert.Contains($"{profiles} encoders.", text, StringComparison.Ordinal);
            Assert.Equal(profiles, Directory.GetDirectories(output, "audio-*").Length);
            string master = await File.ReadAllTextAsync(Path.Combine(output, "index.m3u8"));
            Assert.Equal(profiles, master.Split('\n').Count(line => line.StartsWith("#EXT-X-MEDIA:", StringComparison.Ordinal)));
            string[] variants = master.Split('\n').Where(line => line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal)).ToArray();
            Assert.Equal(3, variants.Length);
            Assert.Contains("AUDIO=\"audio-0\"", variants[0]);
            Assert.Contains("AUDIO=\"audio-0\"", variants[2]);
            if (profiles == 2)
            {
                Assert.Contains("AUDIO=\"audio-1\"", variants[1]);
            }
            else
            {
                Assert.Contains("AUDIO=\"audio-0\"", variants[1]);
            }
            await VerifyPlaylistsAsync(output, 2);
        }

        [Theory]
        [InlineData("--video-only")]
        [InlineData("--audio-stream 1 --video-only")]
        [InlineData("--audio-stream 2")]
        public async Task InvalidSeparateAudioSelectionPublishesNothing(string options)
        {
            List<string> arguments = ["hls", "encode", Data("avc-aac.mp4"), Path.Combine(directory, "invalid"), "--separate-audio"];
            arguments.AddRange(options.Split(' '));
            arguments.AddRange(["--widths", "32"]);
            (int code, _, _) = await CliTests.RunAsync(arguments.ToArray());
            Assert.Equal(2, code);
            Assert.Empty(Directory.GetFileSystemEntries(directory));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DuplicateDimensionsDiscardPreparedAudioAndEveryPartialRendition(bool encode)
        {
            List<string> arguments = ["hls", "encode", Data("avc-aac.mp4"), Path.Combine(directory, "invalid"),
                "--separate-audio", "--audio-stream", "1"];
            if (encode)
            {
                arguments.AddRange(["--audio-codec", "Aac", "--audio-bit-rates", "96000,128000"]);
            }
            arguments.AddRange(["--widths", "128", "256"]);
            (int code, _, _) = await CliTests.RunAsync(arguments.ToArray());
            Assert.Equal(2, code);
            Assert.Empty(Directory.GetFileSystemEntries(directory));
        }

        private static async Task VerifyPlaylistsAsync(string output, int target)
        {
            foreach (string rendition in Directory.GetDirectories(output))
            {
                string playlist = await File.ReadAllTextAsync(Path.Combine(rendition, "index.m3u8"));
                Assert.Contains($"#EXT-X-TARGETDURATION:{target}\n", playlist, StringComparison.Ordinal);
                Assert.EndsWith("#EXT-X-ENDLIST\n", playlist, StringComparison.Ordinal);
                Assert.True(Directory.GetFiles(rendition, "*.ts").Length > 1);
                if (Path.GetFileName(rendition).StartsWith("audio-", StringComparison.Ordinal))
                {
                    Assert.DoesNotContain("#EXT-X-INDEPENDENT-SEGMENTS", playlist, StringComparison.Ordinal);
                }
                else
                {
                    Assert.Contains("#EXT-X-INDEPENDENT-SEGMENTS", playlist, StringComparison.Ordinal);
                }
                foreach (string line in playlist.Split('\n').Where(line => line.StartsWith("#EXTINF:", StringComparison.Ordinal)))
                {
                    decimal seconds = decimal.Parse(line.AsSpan(8).TrimEnd(','), CultureInfo.InvariantCulture);
                    Assert.True(decimal.Round(seconds, 0, MidpointRounding.AwayFromZero) <= target);
                }
            }
        }

        private static string Data(string file) => Path.Combine(AppContext.BaseDirectory, "Data", "Mp4", file);

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
            GC.SuppressFinalize(this);
        }
    }
}
