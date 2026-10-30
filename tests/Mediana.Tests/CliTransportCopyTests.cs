// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Cli;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.IO;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliTransportCopyTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-transport-").FullName;

        [Theory]
        [InlineData("Mp3", "stereo-cbr-id3v23.mp3", "mp3", false)]
        [InlineData("Mp3", "stereo-vbr-cover.mp3", "mp3", true)]
        [InlineData("Mp3", "mono-speech.mp3", "mp3", false)]
        [InlineData("Mp4", "avc-aac.mp4", "aac", true)]
        [InlineData("Mp4", "avc-mp3-44.mp4", "mp3", false)]
        [InlineData("Mp4", "avc-mp3-22.mp4", "mp3", true)]
        public async Task RootAndRemuxCommandsDetectRenamedInputsAndPreserveEveryPacket(string format, string file, string codec, bool namedCommand)
        {
            string input = Path.Combine(directory, "input.bin");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", format, file), input);
            string output = Path.Combine(directory, "output.ts");
            string[] arguments = namedCommand ? ["remux", input, output] : ["-i", input, output];
            (int code, _, string error) = await CliTests.RunAsync(arguments);
            Assert.True(code == 0, error);
            MediaEngine engine = ContainerEngineFactory.CreateBuilder(Mp4TimestampMode.LinearMovie).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            await using ContainerSession session = await engine.OpenContainerAsync(source);
            Assert.Contains(session.Streams, stream => stream.CodecId == codec);
            List<(int Stream, byte[] Data, long Pts, long Dts)> expected = [];
            while (await session.ReadPacketAsync() is MediaPacket packet)
            {
                using (packet)
                {
                    expected.Add((packet.StreamIndex, packet.Data.ToArray(), MpegTsTestInput.Ticks(packet.PresentationTime),
                        MpegTsTestInput.Ticks(packet.DecodeTime)));
                }
            }
            MpegTsTestInput.Verify(await File.ReadAllBytesAsync(output), session.Streams, expected);
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }

        [Fact]
        public async Task ExplicitStreamSelectionCopiesOnlyThatTrack()
        {
            string input = Path.Combine(AppContext.BaseDirectory, "Data", "Mp4", "avc-aac.mp4");
            string output = Path.Combine(directory, "audio.ts");
            (int code, _, string error) = await CliTests.RunAsync(["-i", input, output, "--stream", "1"]);
            Assert.True(code == 0, error);
            MediaEngine engine = ContainerEngineFactory.CreateBuilder(Mp4TimestampMode.LinearMovie).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            await using ContainerSession session = await engine.OpenContainerAsync(source);
            EncodedStreamInfo selected = session.Streams.Single(stream => stream.StreamIndex == 1);
            List<(int Stream, byte[] Data, long Pts, long Dts)> expected = [];
            while (await session.ReadPacketAsync() is MediaPacket packet)
            {
                using (packet)
                {
                    if (packet.StreamIndex == 1)
                    {
                        expected.Add((1, packet.Data.ToArray(), MpegTsTestInput.Ticks(packet.PresentationTime), MpegTsTestInput.Ticks(packet.DecodeTime)));
                    }
                }
            }
            MpegTsTestInput.Verify(await File.ReadAllBytesAsync(output), [selected], expected);
        }

        [Theory]
        [InlineData("Wave", "s16-stereo.wav")]
        [InlineData("Mp3", "vector-8000-1.mp3")]
        public async Task RecognizedButUnsupportedTransportCodecDoesNotProduceMp4ErrorsOrOutput(string format, string file)
        {
            string source = Path.Combine(AppContext.BaseDirectory, "Data", format, file);
            string output = Path.Combine(directory, "output.ts");
            (int code, _, string error) = await CliTests.RunAsync(["-i", source, output]);
            Assert.Equal(3, code);
            Assert.DoesNotContain("Invalid MP4", error, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task InvalidStreamAndUnknownInputPublishNothing()
        {
            string input = Path.Combine(AppContext.BaseDirectory, "Data", "Mp3", "mono-speech.mp3");
            string output = Path.Combine(directory, "output.ts");
            (int invalidStream, _, _) = await CliTests.RunAsync(["-i", input, output, "--stream", "9"]);
            Assert.Equal(2, invalidStream);
            string unknown = Path.Combine(directory, "unknown.mp3");
            await File.WriteAllTextAsync(unknown, "This is not media.");
            (int unknownCode, _, string error) = await CliTests.RunAsync(["-i", unknown, output]);
            Assert.Equal(3, unknownCode);
            Assert.DoesNotContain("Invalid MP4", error, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(directory));
        }

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
            GC.SuppressFinalize(this);
        }
    }
}
