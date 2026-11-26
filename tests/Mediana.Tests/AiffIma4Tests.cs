// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Mediana.Codecs.Pcm;
using Mediana.Containers.Aiff;
using Mediana.Core.Audio;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    public class AiffIma4Tests
    {
        [Theory]
        [InlineData("ima4-mono.aifc", 1, 5696, "749EC3147461D35B69C05A83ADF2FE32124681832C537AFE18C09C94D770FBB1")]
        [InlineData("ima4-stereo.aifc", 2, 22464, "8147D6DD9A74208C58EC552CDF25C9EA6F26D76F7CC9DAAF30603A30675472A7")]
        public async Task DecodedPcmMatchesFfmpegAcrossPacketBoundaries(string name, int channels, int expectedSamples, string expectedHash)
        {
            AiffIma4Codec codec = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new AiffReaderFactory(new() { SamplesPerPacket = 64 }))
                .AddAudioDecoder(codec).AddStreamMetadataReader(codec).Build();
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Aiff", name);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
            MediaInfo info = await engine.ProbeAsync(source);
            TimeBase clock = new(1, 22050);
            Assert.Equal(new MediaDuration(expectedSamples, clock), info.Container!.DeclaredDuration);
            Assert.Equal(codec.CodecId, Assert.Single(info.Streams).CodecId);
            AudioStreamMetadata metadata = Assert.IsType<AudioStreamMetadata>(info.StreamMetadata[0]);
            Assert.Equal(22050, metadata.SampleRate);
            Assert.Equal(channels, metadata.ChannelCount);

            await using AudioSession session = await engine.OpenAudioAsync(source, 0);
            using IncrementalHash pcm = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] encoded = new byte[64 * channels * sizeof(short)];
            int samples = 0;
            while (await session.ReadFrameAsync() is AudioFrame frame)
            {
                using (frame)
                {
                    Assert.Equal(AudioSampleFormat.Signed16, frame.Format.SampleFormat);
                    Assert.Equal(new MediaTimestamp(samples, clock), frame.Timestamp);
                    Assert.Equal(64, frame.SamplesPerChannel);
                    ReadOnlySpan<short> decoded = MemoryMarshal.Cast<byte, short>(frame.GetPlane(0).Span);
                    for (int index = 0; index < decoded.Length; index++)
                    {
                        BinaryPrimitives.WriteInt16LittleEndian(encoded.AsSpan(index * sizeof(short)), decoded[index]);
                    }
                    pcm.AppendData(encoded);
                    samples += frame.SamplesPerChannel;
                }
            }
            Assert.Equal(expectedSamples, samples);
            Assert.Equal(expectedHash, Convert.ToHexString(pcm.GetHashAndReset()));
        }
    }
}
